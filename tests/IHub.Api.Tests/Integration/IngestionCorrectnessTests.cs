using System.Net;
using System.Net.Http.Json;
using IHub.Api.Features.Activities;
using IHub.Api.Tests.Support;

namespace IHub.Api.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class IngestionCorrectnessTests(IHubApiFactory factory)
{
    private readonly ApiClient _api = new(factory.CreateClient());

    [Fact]
    public async Task Resubmitting_the_same_revision_is_counted_once()
    {
        var intermediary = TestActivities.NewIntermediaryId();
        var activity = TestActivities.Build(intermediary, amount: 100m);

        Assert.Equal(IngestOutcome.Created, await _api.SubmitOneAsync(activity));
        Assert.Equal(IngestOutcome.Duplicate, await _api.SubmitOneAsync(activity));
        Assert.Equal(IngestOutcome.Duplicate, await _api.SubmitOneAsync(activity));

        var total = Assert.Single((await _api.GetStatementAsync(intermediary)).Totals);
        Assert.Equal(100m, total.GrossAmount);
        Assert.Equal(1, total.ActivityCount);
    }

    [Fact]
    public async Task Duplicates_within_one_batch_are_counted_once()
    {
        var intermediary = TestActivities.NewIntermediaryId();
        var activity = TestActivities.Build(intermediary, amount: 75m);

        var response = await _api.SubmitAsync(activity, activity);

        Assert.Equal(new[] { IngestOutcome.Created, IngestOutcome.Duplicate }, response.Results.Select(r => r.Outcome));
        Assert.Equal(75m, Assert.Single((await _api.GetStatementAsync(intermediary)).Totals).GrossAmount);
    }

    [Fact]
    public async Task Newer_version_replaces_the_stored_state()
    {
        var intermediary = TestActivities.NewIntermediaryId();
        var id = TestActivities.NewActivityId();

        await _api.SubmitOneAsync(TestActivities.Build(intermediary, id, version: 1, amount: 100m));
        Assert.Equal(IngestOutcome.Updated, await _api.SubmitOneAsync(TestActivities.Build(intermediary, id, version: 2, amount: 250m)));

        var total = Assert.Single((await _api.GetStatementAsync(intermediary)).Totals);
        Assert.Equal(250m, total.GrossAmount);
        Assert.Equal(1, total.ActivityCount);
    }

    [Fact]
    public async Task Reversal_that_arrives_before_the_original_is_not_undone_by_it()
    {
        var intermediary = TestActivities.NewIntermediaryId();
        var id = TestActivities.NewActivityId();

        Assert.Equal(IngestOutcome.Created, await _api.SubmitOneAsync(TestActivities.Build(intermediary, id, version: 2, status: "Reversed")));
        Assert.Equal(IngestOutcome.Stale, await _api.SubmitOneAsync(TestActivities.Build(intermediary, id, version: 1, status: "Active")));

        var total = Assert.Single((await _api.GetStatementAsync(intermediary)).Totals);
        Assert.Equal(0m, total.GrossAmount);
        Assert.Equal(0, total.ActivityCount);
        Assert.Equal(1, total.ReversedActivityCount);
    }

    [Fact]
    public async Task Same_version_with_different_content_is_flagged_as_conflict_and_not_applied()
    {
        var intermediary = TestActivities.NewIntermediaryId();
        var id = TestActivities.NewActivityId();

        await _api.SubmitOneAsync(TestActivities.Build(intermediary, id, version: 1, amount: 100m));
        Assert.Equal(IngestOutcome.Conflict, await _api.SubmitOneAsync(TestActivities.Build(intermediary, id, version: 1, amount: 999m)));

        Assert.Equal(100m, Assert.Single((await _api.GetStatementAsync(intermediary)).Totals).GrossAmount);
    }

    [Fact]
    public async Task Concurrent_submissions_for_one_intermediary_are_all_counted_exactly_once()
    {
        const int activityCount = 40;
        var intermediary = TestActivities.NewIntermediaryId();
        var activities = Enumerable.Range(1, activityCount)
            .Select(i => TestActivities.Build(intermediary, amount: i))
            .ToList();

        await Task.WhenAll(activities.Concat(activities).Select(a => _api.SubmitAsync(a)));

        var total = Assert.Single((await _api.GetStatementAsync(intermediary)).Totals);
        Assert.Equal(activityCount, total.ActivityCount);
        Assert.Equal(Enumerable.Range(1, activityCount).Sum(), total.GrossAmount);
    }

    [Fact]
    public async Task Concurrent_versions_of_one_activity_converge_on_the_highest_version()
    {
        const int versions = 25;
        var intermediary = TestActivities.NewIntermediaryId();
        var id = TestActivities.NewActivityId();

        var submissions = Enumerable.Range(1, versions)
            .SelectMany(v => Enumerable.Repeat(TestActivities.Build(intermediary, id, version: v, amount: v * 10m), 2))
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        var outcomes = await Task.WhenAll(submissions.Select(_api.SubmitOneAsync));

        Assert.Equal(1, outcomes.Count(o => o == IngestOutcome.Created));
        Assert.DoesNotContain(IngestOutcome.Conflict, outcomes);

        var total = Assert.Single((await _api.GetStatementAsync(intermediary)).Totals);
        Assert.Equal(1, total.ActivityCount);
        Assert.Equal(versions * 10m, total.GrossAmount);
    }

    [Fact]
    public async Task Invalid_batch_is_rejected_and_applies_nothing()
    {
        var intermediary = TestActivities.NewIntermediaryId();
        var batch = new SubmitActivitiesRequest(
        [
            TestActivities.Build(intermediary, amount: 10m),
            TestActivities.Build(intermediary, currency: "XXX"),
        ]);

        using var response = await _api.Http.PostAsJsonAsync("/api/v1/activities", batch);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await _api.GetStatementAsync(intermediary)).Totals);
    }

    [Fact]
    public async Task Activity_moved_to_another_intermediary_leaves_the_first_statement()
    {
        var first = TestActivities.NewIntermediaryId();
        var second = TestActivities.NewIntermediaryId();
        var id = TestActivities.NewActivityId();

        await _api.SubmitOneAsync(TestActivities.Build(first, id, version: 1, amount: 100m));
        await _api.SubmitOneAsync(TestActivities.Build(second, id, version: 2, amount: 100m));

        Assert.Empty((await _api.GetStatementAsync(first)).Totals);
        Assert.Equal(100m, Assert.Single((await _api.GetStatementAsync(second)).Totals).GrossAmount);
    }
}
