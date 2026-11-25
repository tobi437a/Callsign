using Callsign.Core.Entities;
using Callsign.Core.Ports;
using Callsign.Core.World;
using Xunit;

namespace Callsign.Core.Tests;

public class EntityScopeTests
{
    sealed class CountingLog : ILog
    {
        public int Errors, Warnings;
        public void Info(string message) { }
        public void Warn(string message) => Warnings++;
        public void Error(string message, Exception? exception = null) => Errors++;
    }

    sealed class Thing(List<string> released, string name, bool throws = false) : ITracked
    {
        public void Release(ReleaseMode mode)
        {
            released.Add(name + ":" + mode);
            if (throws) throw new InvalidOperationException();
        }
    }

    [Fact]
    public void CloseReleasesNewestFirstWithTheGivenMode()
    {
        var released = new List<string>();
        var scope = new EntityScopes(new CountingLog()).Open("test");
        scope.Track(new Thing(released, "ped"));
        scope.Track(new Thing(released, "blip"));

        scope.Close(ReleaseMode.Dismiss);

        Assert.Equal(new[] { "blip:Dismiss", "ped:Dismiss" }, released);
        Assert.True(scope.IsClosed);
        Assert.Equal(0, scope.Count);
    }

    [Fact]
    public void ClosingTwiceReleasesOnce()
    {
        var released = new List<string>();
        var scope = new EntityScopes(new CountingLog()).Open("test");
        scope.Track(new Thing(released, "ped"));

        scope.Close(ReleaseMode.Dismiss);
        scope.Close(ReleaseMode.Delete);

        Assert.Single(released);
    }

    [Fact]
    public void AThrowingReleaseDoesNotStopTheOthers()
    {
        var released = new List<string>();
        var log = new CountingLog();
        var scope = new EntityScopes(log).Open("test");
        scope.Track(new Thing(released, "a"));
        scope.Track(new Thing(released, "b", throws: true));

        scope.Close(ReleaseMode.Delete);

        Assert.Equal(2, released.Count);
        Assert.Equal(1, log.Errors);
    }

    [Fact]
    public void TrackingOnAClosedScopeDeletesImmediately()
    {
        var released = new List<string>();
        var log = new CountingLog();
        var scope = new EntityScopes(log).Open("test");
        scope.Close(ReleaseMode.Dismiss);

        scope.Track(new Thing(released, "late"));

        Assert.Equal(new[] { "late:Delete" }, released);
        Assert.Equal(1, log.Warnings);
    }

    [Fact]
    public void CloseAllClosesOnlyWhatIsStillOpen()
    {
        var released = new List<string>();
        var scopes = new EntityScopes(new CountingLog());
        var done = scopes.Open("done");
        var leaked = scopes.Open("leaked");
        done.Track(new Thing(released, "a"));
        leaked.Track(new Thing(released, "b"));
        done.Close(ReleaseMode.Dismiss);

        int closed = scopes.CloseAll(ReleaseMode.Delete);

        Assert.Equal(1, closed);
        Assert.Equal(new[] { "a:Dismiss", "b:Delete" }, released);
        Assert.Equal(0, scopes.OpenCount);
    }
}

public class PositionTests
{
    [Fact]
    public void DistanceIsEuclidean() =>
        Assert.Equal(5f, new Position(0, 0, 0).DistanceTo(new Position(3, 4, 0)));
}
