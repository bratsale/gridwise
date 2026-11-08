using GridWise.Batch.Services;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace GridWise.Batch.Tests;

public class FailureDecisionTests
{
    [Fact]
    public void Poison_message_goes_to_dlq_at_once()
        => Assert.True(MessageProcessing.Decide(new PoisonMessageException("bad"), 0).DeadLetter);

    [Fact]
    public void Database_outage_waits_5_seconds_and_never_dead_letters()
    {
        foreach (var ex in new Exception[] { new NpgsqlException("down"), new RetryLimitExceededException("down", new Exception()) })
        {
            var decision = MessageProcessing.Decide(ex, failures: 100);

            Assert.False(decision.DeadLetter);
            Assert.Equal(TimeSpan.FromSeconds(5), decision.Wait);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public void Other_failures_are_retried_with_growing_pauses(int failures, int seconds)
    {
        var decision = MessageProcessing.Decide(new InvalidOperationException(), failures);

        Assert.False(decision.DeadLetter);
        Assert.Equal(TimeSpan.FromSeconds(seconds), decision.Wait);
    }

    [Fact]
    public void Other_failure_goes_to_dlq_after_third_attempt()
        => Assert.True(MessageProcessing.Decide(new InvalidOperationException(), 3).DeadLetter);

    [Fact]
    public void A_database_error_with_a_sql_state_is_not_an_outage()
        => Assert.False(MessageProcessing.IsDatabaseDown(new PostgresException("x", "ERROR", "ERROR", "23505")));
}
