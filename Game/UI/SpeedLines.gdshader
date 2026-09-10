shader_type canvas_item;

// 径向冲刺速度线 + 暗角（M5 §2，纯程序化，无贴图无美术资产）。
// intensity 由 CameraFeel 的冲锋/滞空混合度驱动：控制线条扇区可见数、亮度与暗角。

uniform float intensity : hint_range(0.0, 1.0) = 0.0;
uniform float density = 48.0; // 径向扇区数（线条密度上限）
uniform float speed = 2.5; // 线条沿半径流动速度
uniform float vignette : hint_range(0.0, 1.0) = 0.35; // 暗角强度

float hash(float n)
{
    return fract(sin(n * 127.1) * 43758.5453);
}

void fragment()
{
    vec2 uv = SCREEN_UV * 2.0 - 1.0;
    uv.x *= 1.7; // 宽屏角度校正
    float r = length(uv);
    float ang = atan(uv.y, uv.x);

    // 角度哈希：每个扇区一条线，随机长度相位与流速
    float sector = floor((ang / 6.2831853 + 0.5) * density);
    float rnd = hash(sector);

    // 径向断裂条纹：沿半径方向向屏幕边缘流动
    float flow = fract(r * (0.6 + rnd * 0.8) - TIME * speed * (0.5 + rnd));
    float stripe = step(1.0 - 0.35, flow);

    // 线条只出现在中半径以外向边缘增强；intensity 越高可见扇区越多、越亮
    float radial = smoothstep(0.25, 0.9, r);
    float visible = step(rnd, intensity * 1.15);
    float line = stripe * radial * visible * intensity;

    // 同文件内置轻微暗角（随 intensity 渐入）
    float vig = smoothstep(0.55, 1.25, r) * vignette * intensity;

    float alpha = clamp(line + vig, 0.0, 1.0);
    COLOR = vec4(vec3(line), alpha);
}
