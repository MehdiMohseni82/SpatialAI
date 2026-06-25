using FluentAssertions;
using SpatialAI.Api.Spaces;
using SpatialAI.Core.Scene;
using SpatialAI.Core.Tools;
using Xunit;

namespace SpatialAI.Tests;

public class SpaceRepositoryTests : IDisposable
{
    private readonly string _dir;
    private readonly SpaceRepository _repo;

    public SpaceRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "spatialai-tests", Guid.NewGuid().ToString("N"));
        _repo = new SpaceRepository(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static SpaceRecord BuildSampleSpace(string name)
    {
        var store = new SceneStore();
        var tools = new SceneTools(store);
        tools.CreateRoom("Studio", 6, 5, windows: 2, doors: 1);
        tools.CreateItem("Chair", "chair", colorR: 0.2f, colorG: 0.4f, colorB: 0.7f);
        return new SpaceRecord { Name = name, Scene = store.Current };
    }

    [Fact]
    public void Save_List_Load_RoundTripsScene_WithPartsIntact()
    {
        var record = BuildSampleSpace("My Office");
        _repo.Save(record);

        var list = _repo.List();
        list.Should().ContainSingle().Which.Name.Should().Be("My Office");

        var loaded = _repo.Load(record.Id);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("My Office");

        var room = loaded.Scene.Rooms.Should().ContainSingle().Subject;
        room.Width.Should().Be(6);
        room.Openings.Count(o => o.Type == "window").Should().Be(2);
        room.Openings.Count(o => o.Type == "door").Should().Be(1);

        var chair = loaded.Scene.Items.Should().ContainSingle().Subject;
        chair.Name.Should().Be("Chair");
        chair.Kind.Should().Be("chair");
        chair.Parts.Should().NotBeEmpty(); // composite parts survive the round-trip
    }

    [Fact]
    public void Rename_ChangesName_AndPersists()
    {
        var record = BuildSampleSpace("Before");
        _repo.Save(record);

        _repo.Rename(record.Id, "After").Should().BeTrue();

        _repo.Load(record.Id)!.Name.Should().Be("After");
    }

    [Fact]
    public void Delete_RemovesSpace()
    {
        var record = BuildSampleSpace("Temp");
        _repo.Save(record);

        _repo.Delete(record.Id).Should().BeTrue();

        _repo.Exists(record.Id).Should().BeFalse();
        _repo.List().Should().BeEmpty();
    }
}
