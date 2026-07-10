using FluentAssertions;
using SpatialAI.Api.Spaces;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;
using Xunit;

namespace SpatialAI.Tests;

public class SpaceManagerTests : IDisposable
{
    private readonly string _dir;
    private readonly SceneStore _store;
    private readonly SceneTools _tools;
    private readonly SpaceManager _manager;

    public SpaceManagerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "spatialai-tests", Guid.NewGuid().ToString("N"));
        _store = new SceneStore();
        _tools = new SceneTools(_store);
        _manager = new SpaceManager(_store, new SpaceRepository(_dir));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void NewSpace_ClearsScene_AndIsUnsaved()
    {
        _tools.CreateRoom("Office", 6, 5);

        var info = _manager.NewSpace("Fresh");

        info.Name.Should().Be("Fresh");
        info.Saved.Should().BeFalse();
        _store.Current.Rooms.Should().BeEmpty();
    }

    [Fact]
    public void SaveAs_PersistsAndBecomesCurrent()
    {
        _tools.CreateRoom("Office", 6, 5);

        var info = _manager.SaveAs("Office Space");

        info.Name.Should().Be("Office Space");
        info.Saved.Should().BeTrue();
        _manager.List().Should().ContainSingle().Which.Name.Should().Be("Office Space");
        _manager.Current.Id.Should().Be(info.Id);
    }

    [Fact]
    public void Open_SwapsActiveScene()
    {
        _tools.CreateRoom("Office", 6, 5);
        var office = _manager.SaveAs("Office");

        _manager.NewSpace("Empty");
        _store.Current.Rooms.Should().BeEmpty();

        var reopened = _manager.Open(office.Id);

        reopened.Should().NotBeNull();
        reopened!.Name.Should().Be("Office");
        _store.Current.Rooms.Should().ContainSingle().Which.Name.Should().Be("Office");
    }

    [Fact]
    public void OpenByName_ResolvesCaseInsensitively()
    {
        _tools.CreateRoom("Kitchen", 4, 4);
        _manager.SaveAs("Kitchen");
        _manager.NewSpace("Empty");

        var reopened = _manager.OpenByName("kitchen");

        reopened.Should().NotBeNull();
        _store.Current.Rooms.Should().ContainSingle().Which.Name.Should().Be("Kitchen");
    }

    [Fact]
    public void ClearChat_EmptiesTheTranscript_AndPersistsForASavedSpace()
    {
        _manager.AppendChat(new[] { new ChatMessage("user", "hi"), new ChatMessage("ai", "hello") });
        _manager.SaveAs("Chatty");                 // persist so we also cover the re-write path
        _manager.CurrentChat().Should().HaveCount(2);

        _manager.ClearChat();

        _manager.CurrentChat().Should().BeEmpty();
        _manager.Open(_manager.Current.Id)!.Name.Should().Be("Chatty");
        _manager.CurrentChat().Should().BeEmpty();  // stays empty after reopening the saved space
    }

    [Fact]
    public void List_And_Delete()
    {
        _tools.CreateRoom("A", 3, 3);
        var a = _manager.SaveAs("A");
        _manager.NewSpace("B");
        _tools.CreateRoom("B", 3, 3);
        _manager.SaveAs("B");

        _manager.List().Should().HaveCount(2);

        _manager.Delete(a.Id).Should().BeTrue();
        _manager.List().Should().ContainSingle().Which.Name.Should().Be("B");
    }
}
