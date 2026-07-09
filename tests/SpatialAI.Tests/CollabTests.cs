using FluentAssertions;
using SpatialAI.Api.Collab;
using SpatialAI.Api.Tenancy;
using Xunit;

namespace SpatialAI.Tests;

public class CollabTests
{
    private static TenantRegistry NewRegistry() =>
        new(Path.Combine(Path.GetTempPath(), "spatialai-tests", Guid.NewGuid().ToString("N")));

    [Fact]
    public void Create_seeds_the_hosts_scene_into_the_shared_room()
    {
        var reg = NewRegistry();
        var host = reg.For("host-1");
        host.Tools.CreateRoom("Office", 5, 5);          // build in the host's personal scene

        var svc = new RoomService(reg);
        var code = svc.Create("host-1", "Ada");

        var roomCtx = reg.For(RoomService.TenantKey(code));
        roomCtx.Store.Current.Rooms.Should().ContainSingle(r => r.Name == "Office");
        // A deep clone, not a shared reference: editing the room must not touch the host's scene.
        roomCtx.Tools.CreateRoom("Extra", 3, 3);
        host.Store.Current.Rooms.Should().ContainSingle();   // host still has only "Office"
    }

    [Fact]
    public void Join_and_leave_track_the_roster_and_evict_empty_rooms()
    {
        var reg = NewRegistry();
        var svc = new RoomService(reg);
        var code = svc.Create("host-1", "Ada");         // host auto-joins

        svc.Join(code, "user-2", "Bob").Should().NotBeNull();
        svc.ParticipantCount(code).Should().Be(2);

        svc.Leave(code, "user-2");
        svc.ParticipantCount(code).Should().Be(1);

        svc.Leave(code, "host-1");                      // last one out
        svc.Exists(code).Should().BeFalse();            // empty room evicted
    }

    [Fact]
    public void Join_unknown_room_returns_null()
    {
        var svc = new RoomService(NewRegistry());
        svc.Join("NOPE12", "u", "n").Should().BeNull();
    }

    [Fact]
    public void Subscribe_delivers_an_immediate_presence_baseline()
    {
        var reg = NewRegistry();
        var svc = new RoomService(reg);
        var code = svc.Create("host-1", "Ada");

        var sub = svc.Subscribe(code);
        sub.Should().NotBeNull();
        sub!.Value.reader.TryRead(out var baseline).Should().BeTrue();
        baseline.Should().Contain("presence");
        baseline.Should().Contain("Ada");
    }

    [Fact]
    public void IsHost_reflects_the_creator()
    {
        var reg = NewRegistry();
        var svc = new RoomService(reg);
        var code = svc.Create("host-1", "Ada");
        svc.Join(code, "user-2", "Bob");

        svc.IsHost(code, "host-1").Should().BeTrue();
        svc.IsHost(code, "user-2").Should().BeFalse();
    }
}
