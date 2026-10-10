using GT67.Utils;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>
/// Hardware occlusion culling without stalls. After the visible objects are drawn, the bounding box of each candidate
/// is rasterised against the depth buffer (no colour, no depth writes) inside an occlusion query. The answer is read on
/// a later frame, only once the GPU has it: a box that produced no pixels marks its object hidden, and it is skipped
/// until a later query sees it again. An object coming out from behind a wall can appear a frame late; objects the
/// camera is inside or close to are never hidden.
/// </summary>
public sealed class OcclusionCuller : IDisposable
{
    private sealed class State
    {
        public int Query;
        public bool Pending;
        public bool Hidden;
        public int LastSeenFrame;
    }

    private readonly Dictionary<GameObject, State> _states = new();
    private readonly List<GameObject> _stale = new();
    private readonly Mesh _box = Primitives.Cube(1f);
    private int _frame;

    /// <summary>
    /// Objects with fewer triangles than this are always drawn: testing their box would cost about as much as drawing
    /// them.
    /// </summary>
    public int MinTriangles { get; set; } = 200;

    /// <summary>Starts a frame: collects the query answers the GPU has finished, and forgets objects no longer seen.</summary>
    public void BeginFrame()
    {
        _frame++;
        foreach (var (obj, state) in _states)
        {
            if (state.Pending)
            {
                GL.GetQueryObject(state.Query, GetQueryObjectParam.QueryResultAvailable, out int available);
                if (available != 0)
                {
                    GL.GetQueryObject(state.Query, GetQueryObjectParam.QueryResult, out int samples);
                    state.Hidden = samples == 0;
                    state.Pending = false;
                }
            }
            if (obj.IsDestroyed || _frame - state.LastSeenFrame > 120) _stale.Add(obj);
        }
        foreach (var obj in _stale)
        {
            GL.DeleteQuery(_states[obj].Query);
            _states.Remove(obj);
        }
        _stale.Clear();
    }

    /// <summary>True if the last finished query found the object completely hidden.</summary>
    public bool IsHidden(GameObject obj, Vector3 eye, float near)
    {
        if (!_states.TryGetValue(obj, out var state)) return false;
        state.LastSeenFrame = _frame;
        return state.Hidden && !IsClose(obj.Bounds, eye, near);
    }

    /// <summary>
    /// Issues a query for each candidate (those drawn and those skipped) whose previous answer is in. Call right after
    /// the opaque objects are drawn, with their depth in the bound framebuffer.
    /// </summary>
    public void IssueQueries(IReadOnlyList<GameObject> candidates, Shader depthShader, Matrix4 viewProjection, Vector3 eye, float near)
    {
        bool any = false;
        foreach (var obj in candidates)
        {
            if (!IsCandidate(obj)) continue;
            var bounds = obj.Bounds;
            if (!_states.TryGetValue(obj, out var state))
            {
                state = new State { Query = GL.GenQuery() };
                _states[obj] = state;
            }
            state.LastSeenFrame = _frame;
            if (state.Pending) continue;   // still waiting for the GPU: ask again once it answers

            if (IsClose(bounds, eye, near))
            {
                state.Hidden = false;
                continue;
            }

            if (!any)
            {
                any = true;
                depthShader.Use();
                depthShader.SetMatrix4("uLightSpace", viewProjection);
                GL.ColorMask(false, false, false, false);
                GL.DepthMask(false);
                GL.Disable(EnableCap.CullFace);   // a box seen from inside still has to count
            }

            // A little larger than the object, so its own surface never hides its box.
            var size = Vector3.ComponentMax(bounds.Size, new Vector3(0.01f)) * 1.02f;
            depthShader.SetMatrix4("uModel", Matrix4.CreateScale(size) * Matrix4.CreateTranslation(bounds.Center));
            GL.BeginQuery(QueryTarget.AnySamplesPassedConservative, state.Query);
            _box.Draw();
            GL.EndQuery(QueryTarget.AnySamplesPassedConservative);
            state.Pending = true;
        }

        if (any)
        {
            GL.ColorMask(true, true, true, true);
            GL.DepthMask(true);
        }
    }

    public bool IsCandidate(GameObject obj)
    {
        int triangles = 0;
        foreach (var part in obj.Model.Parts) triangles += part.Mesh.Indices.Length / 3;
        return triangles >= MinTriangles;
    }

    /// <summary>The camera is inside the box, or so close that the near plane could cut it: always draw it.</summary>
    private static bool IsClose(Aabb bounds, Vector3 eye, float near)
    {
        float margin = near * 2f + 0.05f;
        return eye.X > bounds.Min.X - margin && eye.X < bounds.Max.X + margin &&
               eye.Y > bounds.Min.Y - margin && eye.Y < bounds.Max.Y + margin &&
               eye.Z > bounds.Min.Z - margin && eye.Z < bounds.Max.Z + margin;
    }

    public void Dispose()
    {
        foreach (var state in _states.Values) GL.DeleteQuery(state.Query);
        _states.Clear();
        _box.Dispose();
    }
}
