using System;

namespace UnityEngine.Rendering.PostProcessing;

[Serializable]
[PostProcess(typeof(UnityEngine.Rendering.PostProcessing.GrainRenderer), "Unity/Grain", true)]
public sealed class Grain : PostProcessEffectSettings
{
	[Tooltip("Enable the use of colored grain.")]
	public BoolParameter colored = new BoolParameter
	{
		value = true
	};

	[Tooltip("Grain strength. Higher values mean more visible grain.")]
	[Range(0f, 1f)]
	public FloatParameter intensity = new FloatParameter
	{
		value = 0f
	};

	[Range(0.3f, 3f)]
	[Tooltip("Grain particle size.")]
	public FloatParameter size = new FloatParameter
	{
		value = 1f
	};

	[DisplayName("Luminance Contribution")]
	[Tooltip("Controls the noise response curve based on scene luminance. Lower values mean less noise in dark areas.")]
	[Range(0f, 1f)]
	public FloatParameter lumContrib = new FloatParameter
	{
		value = 0.8f
	};

	public override bool IsEnabledAndSupported(PostProcessRenderContext context)
	{
		if (enabled.value)
		{
			return intensity.value > 0f;
		}
		return false;
	}
}
