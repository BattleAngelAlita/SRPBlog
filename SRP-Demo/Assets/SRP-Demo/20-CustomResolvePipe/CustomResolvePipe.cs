using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

[ExecuteInEditMode]
public class CustomResolvePipe : RenderPipelineAsset
{
	public enum MSAA
	{
		None = 1,
		x2 = 2,
		x4 = 4
	}

	public MSAA msaaSamples = MSAA.x4;
	public Material customResolve;

#if UNITY_EDITOR
	[UnityEditor.MenuItem("SRP-Demo/20 - Custom Resolve Pipeline")]
	static void CreateBasicAssetPipeline()
	{
		var instance = ScriptableObject.CreateInstance<CustomResolvePipe>();
		UnityEditor.AssetDatabase.CreateAsset(instance, "Assets/SRP-Demo/20-CustomResolvePipe/CustomResolvePipe.asset");
	}
#endif

	protected override IRenderPipeline InternalCreatePipeline()
	{
		return new CustomResolvePipeInstance(this);
	}
}

public class CustomResolvePipeInstance : RenderPipeline
{
	CustomResolvePipe pipeAsset;

	public CustomResolvePipeInstance(CustomResolvePipe pipeAsset)
	{
		this.pipeAsset = pipeAsset;
	}

	public override void Render(ScriptableRenderContext context, Camera[] cameras)
	{
		base.Render(context, cameras);

		foreach(var camera in cameras)
		{
			// Culling
			ScriptableCullingParameters cullingParams;
			if(!CullResults.GetCullingParameters(camera, out cullingParams))
				continue;

			CullResults cull = CullResults.Cull(ref cullingParams, context);

			// Setup camera for rendering (sets render target, view/projection matrices and other
			// per-camera built-in shader variables).
			context.SetupCameraProperties(camera);

			int depthBuffer = Shader.PropertyToID("_DepthBuffer");
			RenderTargetIdentifier  depthBufferID   = new RenderTargetIdentifier(depthBuffer);
			RenderTextureDescriptor depthBufferDesc = new RenderTextureDescriptor(Screen.width, Screen.height, RenderTextureFormat.Depth, 32);
			depthBufferDesc.msaaSamples = 4;
			depthBufferDesc.bindMS = true;

			int colorBuffer = Shader.PropertyToID("_ColorBuffer");
			RenderTargetIdentifier  colorBufferID   = new RenderTargetIdentifier(colorBuffer);
			RenderTextureDescriptor colorBufferDesc = new RenderTextureDescriptor(Screen.width, Screen.height, RenderTextureFormat.DefaultHDR, 0);
			colorBufferDesc.msaaSamples = 4;
			colorBufferDesc.bindMS = true;

			// clear depth buffer
			var cmd = new CommandBuffer();
			cmd.GetTemporaryRT(depthBuffer, depthBufferDesc, FilterMode.Point);
			cmd.GetTemporaryRT(colorBuffer, colorBufferDesc, FilterMode.Point);
			cmd.SetRenderTarget(colorBufferID, depthBufferID);

			cmd.ClearRenderTarget(true, true, Color.clear);
			context.ExecuteCommandBuffer(cmd);
			cmd.Release();

			// Draw opaque objects using BasicPass shader pass
			var settings = new DrawRendererSettings(camera, new ShaderPassName("BasicPass"));
			settings.sorting.flags = SortFlags.CommonOpaque;

			var filterSettings = new FilterRenderersSettings(true) { renderQueueRange = RenderQueueRange.opaque };
			context.DrawRenderers(cull.visibleRenderers, ref settings, filterSettings);

			// Draw skybox
			context.DrawSkybox(camera);

			// Draw transparent objects using BasicPass shader pass
			settings.sorting.flags = SortFlags.CommonTransparent;
			filterSettings.renderQueueRange = RenderQueueRange.transparent;
			context.DrawRenderers(cull.visibleRenderers, ref settings, filterSettings);

			var cmdResolve = new CommandBuffer();
			cmdResolve.SetGlobalTexture(depthBuffer, depthBufferID);
			cmdResolve.Blit(null, BuiltinRenderTextureType.CameraTarget, pipeAsset.customResolve);

			context.ExecuteCommandBuffer(cmdResolve);
			cmdResolve.Release();

			var cmdCleanup = new CommandBuffer();
			cmdCleanup.ReleaseTemporaryRT(depthBuffer);
			cmdCleanup.ReleaseTemporaryRT(colorBuffer);
			
			context.ExecuteCommandBuffer(cmdCleanup);
			cmdCleanup.Release();

			context.Submit();
		}
	}
}
