using SpatialAI.Core.Model;

namespace SpatialAI.Core.Scene;

/// <summary>
/// Thread-safe in-memory holder for the current <see cref="Model.Scene"/>.
/// Raises <see cref="Changed"/> after every mutation so the viewer can update live.
/// </summary>
public sealed class SceneStore
{
    private readonly Lock _gate = new();
    private Model.Scene _scene = new();

    /// <summary>Fired (outside the lock) whenever the scene changes.</summary>
    public event Action? Changed;

    /// <summary>Runs <paramref name="action"/> under the lock, then notifies listeners.</summary>
    public T Mutate<T>(Func<Model.Scene, T> action)
    {
        T result;
        lock (_gate)
        {
            result = action(_scene);
        }
        Changed?.Invoke();
        return result;
    }

    /// <summary>Reads from the scene under the lock without notifying.</summary>
    public T Read<T>(Func<Model.Scene, T> reader)
    {
        lock (_gate)
        {
            return reader(_scene);
        }
    }

    /// <summary>Returns a snapshot reference of the current scene (for serialization).</summary>
    public Model.Scene Current
    {
        get { lock (_gate) { return _scene; } }
    }

    /// <summary>Replaces the scene with an empty one and notifies.</summary>
    public void Reset()
    {
        lock (_gate) { _scene = new Model.Scene(); }
        Changed?.Invoke();
    }

    /// <summary>Swaps in <paramref name="scene"/> as the active scene and notifies (e.g. opening a saved space).</summary>
    public void Load(Model.Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        lock (_gate) { _scene = scene; }
        Changed?.Invoke();
    }
}
