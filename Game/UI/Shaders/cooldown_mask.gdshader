shader_type canvas_item;

// 径向冷却遮罩（M8）：progress = 剩余冷却比例（1=刚施放全暗 → 0=就绪全亮）
uniform float progress : hint_range(0.0, 1.0) = 0.0;

void fragment()
{
	vec2 centered = UV - vec2(0.5);
	// UV.y 向下：atan 0=右、+π/2=下（顺时针增）；+π/2 平移使 0=顶部
	float t = fract((atan(centered.y, centered.x) + PI * 0.5) / TAU);
	float masked = step(t, progress); // 顶部扇区先亮起来，暗罩顺时针收缩
	vec4 dim = vec4(0.0, 0.0, 0.0, 0.55);
	COLOR = mix(COLOR, dim, masked);
}
