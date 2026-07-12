using System.Buffers;
using System.Collections;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NFMWorldLibrary;

namespace NFMWorld;

public class Scene
{
    private readonly GraphicsDevice _graphicsDevice;
    private Camera _camera;
    private readonly Camera[] _lightCameras;
    public readonly List<GameObject> Objects;
    private readonly RenderDataCache _renderDataCache;

    public Camera ActiveCamera
    {
        get => _camera;
        set => _camera = value;
    }

    public Scene(GraphicsDevice graphicsDevice, IEnumerable<GameObject> objects, Camera camera, Camera[] lightCameras)
    {
        _graphicsDevice = graphicsDevice;
        _camera = camera;
        _lightCameras = lightCameras;
        Objects = [..objects];
        _renderDataCache = new RenderDataCache(graphicsDevice);
    }
    
    public void Render(float alpha, bool useShadowMapping, bool clearRenderBuffer = true)
    {
        _camera.OnBeforeRender(alpha);
        foreach (var lightCamera in _lightCameras)
        {
            lightCamera.OnBeforeRender(alpha);
        }
        
        foreach (var renderable in Objects)
        {
            renderable.OnBeforeRender(alpha);
        }
        
        _graphicsDevice.BlendState = BlendState.Opaque;
        _graphicsDevice.DepthStencilState = DepthStencilState.Default;

        // CREATE SHADOW MAP
        var totalCascades = Math.Min(_lightCameras.Length, WorldGame.NumCascades);

        if (useShadowMapping)
        {
            for (var cascade = 0; cascade < totalCascades; cascade++)
            {
                // Set our render target to our floating point render target
                _graphicsDevice.SetRenderTarget(WorldGame.shadowRenderTargets[cascade]);

                // Clear the render target to white or all 1's
                // We set the clear to white since that represents the 
                // furthest the object could be away
                _graphicsDevice.Clear(Color.White);

                RenderInternal(true, cascade, totalCascades);
            }
            
            _graphicsDevice.SetRenderTarget(null);
        }

        // DRAW WITH SHADOW MAP
        
        if (clearRenderBuffer)
            _graphicsDevice.Clear(Color.CornflowerBlue);

        for (var i = 0; i < 16; i++)
            _graphicsDevice.SamplerStates[i] = SamplerState.PointClamp;

        RenderInternal(totalCascades: totalCascades);

    }
    
    private class RenderDataCache(GraphicsDevice graphicsDevice) : IEnumerable<(DynamicVertexBuffer Buffer, int InstanceCount, IInstancedRenderElement Element)>
    {
        private class CachedRenderData(
            List<RenderData> renderData
        )
        {
            public List<RenderData> RenderData = renderData;
            public List<RenderData> OldRenderData = [];
            public DynamicVertexBuffer? VertexBuffer = null;
            public int HashCode = 0;
        }

        // dict of render order -> (dict of render element -> cached render data)
        private SortedDictionary<int, Dictionary<IInstancedRenderElement, CachedRenderData>> _cache = new();

        ~RenderDataCache()
        {
            foreach (var (_, innerCache) in _cache)
            foreach (var (_, data) in innerCache)
            {
                data.VertexBuffer?.Dispose();
            }
        }

        private static int GetHashCode(ReadOnlySpan<RenderData> renderData)
        {
            var hc = renderData.Length;
            foreach (var val in renderData)
            {
                hc = unchecked(hc * 314159 + val.GetHashCode());
            }
            return hc;
        }
        
        private static bool AreRenderDataListsEqual(ReadOnlySpan<RenderData> a, ReadOnlySpan<RenderData> b, int aHashCode, int bHashCode)
        {
            if (aHashCode != bHashCode)
                return false;
            if (a.Length != b.Length)
                return false;
            for (var i = 0; i < a.Length; i++)
            {
                if (!a[i].Equals(b[i]))
                    return false;
            }
            return true;
        }

        private readonly List<IInstancedRenderElement> _elementsToPrune = new();
        public void Clear()
        {
            // Delete any instance not rendered for two consecutive frames.
            foreach (var (renderOrder, innerCache) in _cache)
            {
                _elementsToPrune.Clear();

                foreach (var (element, data) in innerCache)
                {
                    if (data.RenderData.Count == 0)
                    {
                        _elementsToPrune.Add(element);
                    }
                    else
                    {
                        CollectionsMarshal.SetCount(data.RenderData, 0);
                    }
                }

                foreach (var element in _elementsToPrune)
                {
                    if (innerCache.TryGetValue(element, out var data))
                    {
                        data.VertexBuffer?.Dispose();
                        innerCache.Remove(element);
                    }
                }
            }

        }

        public void Add(RenderData renderData)
        {
            if (!_cache.TryGetValue(renderData.RenderOrder, out var innerCache))
            {
                _cache[renderData.RenderOrder] = innerCache = new Dictionary<IInstancedRenderElement, CachedRenderData>();
            }
            
            ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(innerCache, renderData.RenderElement, out var exists);
            if (!exists)
            {
                entry = new CachedRenderData([renderData]);
            }
            else
            {
                entry!.RenderData.Add(renderData);
            }
        }

        public IEnumerator<(DynamicVertexBuffer Buffer, int InstanceCount, IInstancedRenderElement Element)> GetEnumerator()
        {
            foreach (var (renderOrder, innerCache) in _cache)
            foreach (var (renderElement, cachedRenderData) in innerCache)
            {
                var instances = cachedRenderData.RenderData;
                if (instances.Count == 0) continue;
                
                var oldInstances = cachedRenderData.OldRenderData;
                
                var currentHashCode = GetHashCode(CollectionsMarshal.AsSpan(instances));
                var oldHashCode = cachedRenderData.HashCode;
                
                if (cachedRenderData.VertexBuffer == null ||
                    !AreRenderDataListsEqual(
                        CollectionsMarshal.AsSpan(instances),
                        CollectionsMarshal.AsSpan(oldInstances),
                        currentHashCode,
                        oldHashCode
                    ))
                {
                    using var instanceDataArray = MemoryPool<InstanceData>.Shared.Rent(instances.Count);
                    var instanceDataArraySpan = instanceDataArray.Memory.Span.Slice(0, instances.Count);
                    var i = 0;
                    foreach (var renderData in instances)
                    {
                        instanceDataArraySpan[i++] = renderData.ToInstanceData();
                    }

                    if (cachedRenderData.VertexBuffer == null || cachedRenderData.VertexBuffer.VertexCount < instances.Count)
                    {
                        cachedRenderData.VertexBuffer?.Dispose();
                        cachedRenderData.VertexBuffer = new DynamicVertexBuffer(graphicsDevice, InstanceData.InstanceDeclaration, instances.Count, BufferUsage.WriteOnly)
                        {
                            Name = "Instance Data Vertex Buffer",
                            Tag = this
                        };
                    }

                    cachedRenderData.VertexBuffer.SetDataEXT(instanceDataArraySpan, SetDataOptions.Discard);
                    cachedRenderData.HashCode = currentHashCode;
                    
                    // Swap old and new instance lists
                    CollectionsMarshal.SetCount(oldInstances, instances.Count);
                    CollectionsMarshal.AsSpan(instances).CopyTo(CollectionsMarshal.AsSpan(oldInstances));
                }
                yield return (cachedRenderData.VertexBuffer, instances.Count, renderElement);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
    
    private void RenderInternal(bool isCreateShadowMap = false, int numCascade = -1, int totalCascades = 3)
    {
        var lighting = new Lighting(_lightCameras, WorldGame.shadowRenderTargets, isCreateShadowMap, numCascade, totalCascades);

        _renderDataCache.Clear();
        foreach (var obj in Objects)
        {
            obj.Render(_camera, lighting);
            
            foreach (var renderData in obj.GetRenderData(lighting))
            {
                if (!TryApplyLineDistanceMode(renderData, _camera, out var adjustedRenderData))
                {
                    continue;
                }

                _renderDataCache.Add(adjustedRenderData);
            }
        }

        foreach (var (buffer, instanceCount, element) in _renderDataCache)
        {
            element.Render(_camera, lighting, buffer, instanceCount);
        }
    }

    private static bool TryApplyLineDistanceMode(RenderData renderData, Camera camera, out RenderData adjustedRenderData)
    {
        adjustedRenderData = renderData;

        if (camera is not PerspectiveCamera ||
            renderData.RenderElement is not LineMesh ||
            World.LineDistanceMode == OutlineDistanceMode.Fixed)
        {
            return true;
        }

        var cutoff = World.OutlineCullDistance;
        if (cutoff <= 0)
        {
            return true;
        }

        var distanceSquared = Vector3.DistanceSquared(renderData.World.Translation, camera.Position);
        if (World.LineDistanceMode == OutlineDistanceMode.NfmStyleCull)
        {
            return distanceSquared <= cutoff * cutoff;
        }

        if (World.LineDistanceMode == OutlineDistanceMode.DynamicLineSize)
        {
            var distance = MathF.Sqrt(distanceSquared);
            var distanceAmount = MathHelper.Clamp(distance / cutoff, 0.0f, 1.0f);
            var thicknessScale = MathHelper.Lerp(1.0f, World.OutlineDynamicMinimumScale, distanceAmount);
            adjustedRenderData = renderData with { LineThicknessScale = thicknessScale };
        }

        return true;
    }

    public void OnBeforeUpdate()
    {
        _camera.OnBeforeGameTick();
        foreach (var lightCamera in _lightCameras)
        {
            lightCamera.OnBeforeGameTick();
        }
    }

    public void GameTick(IStage currentStage)
    {
        foreach (var obj in Objects)
        {
            obj.GameTick(currentStage);
        }
    }
}
