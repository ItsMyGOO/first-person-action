# 占位音效生成器（Tools）——真实音频资产到位前的合成占位。
# 重新生成：godot --headless --path . -s Tools/generate_placeholder_sfx.gd
# 输出：Game/Audio/*.wav（16-bit 单声道 44.1kHz，RIFF/WAVE）
# 参数语义：dur 时长秒 / freq 正弦基频（0=纯噪声）/ decay 衰减率 /
#           noise 噪声占比 / sweep 挥砍类扫频包络 / hi 高亮度噪声 / amp 峰值幅度
extends SceneTree

const OUT_DIR := "res://Game/Audio"
const RATE := 44100

const SOUNDS := {
	"swing": {"dur": 0.18, "freq": 0.0, "decay": 22.0, "noise": 1.0, "sweep": true, "amp": 0.55},
	"dash": {"dur": 0.16, "freq": 0.0, "decay": 26.0, "noise": 1.0, "sweep": true, "hi": true, "amp": 0.5},
	"hit_melee": {"dur": 0.14, "freq": 95.0, "decay": 30.0, "noise": 0.5, "amp": 0.8},
	"hit_arrow": {"dur": 0.09, "freq": 220.0, "decay": 45.0, "noise": 0.6, "amp": 0.6},
	"arrow_release": {"dur": 0.12, "freq": 320.0, "decay": 38.0, "noise": 0.7, "amp": 0.45},
	"execute": {"dur": 0.32, "freq": 55.0, "decay": 12.0, "noise": 0.55, "amp": 0.9},
	"hurt": {"dur": 0.16, "freq": 140.0, "decay": 25.0, "noise": 0.4, "amp": 0.7},
	"enemy_die": {"dur": 0.26, "freq": 80.0, "decay": 14.0, "noise": 0.5, "amp": 0.75},
	"ui_click": {"dur": 0.05, "freq": 900.0, "decay": 70.0, "noise": 0.15, "amp": 0.35},
}


func _init() -> void:
	for key in SOUNDS:
		var path := "%s/%s.wav" % [OUT_DIR, key]
		var pcm := _synth(SOUNDS[key])
		_write_wav(path, pcm)
		print("Generated %s (%d samples)" % [path, pcm.size() / 2])
	quit()


func _synth(p: Dictionary) -> PackedByteArray:
	var n: int = int(p["dur"] * RATE)
	var data := PackedByteArray()
	data.resize(n * 2)
	var rng := RandomNumberGenerator.new()
	rng.seed = 20260925
	var noise_state := 0.0
	var smooth_k := 0.25 if not p.get("hi", false) else 0.5
	for i in n:
		var t := float(i) / RATE
		var env: float = exp(-p["decay"] * t)
		var s := 0.0
		var freq: float = p["freq"]
		if freq > 0.0:
			s += sin(TAU * freq * t) * (1.0 - p["noise"]) * env
		if p["noise"] > 0.0:
			# 低通噪声：running-average 平滑白噪
			var white := rng.randf() * 2.0 - 1.0
			noise_state = lerpf(noise_state, white, smooth_k)
			if p.get("sweep", false):
				# 挥砍呜啸：幅度前起后落（sin 半周期包络），不做衰减
				var sweep_env: float = sin(PI * clampf(t / p["dur"], 0.0, 1.0))
				s += noise_state * p["noise"] * sweep_env * 1.5
			else:
				s += noise_state * p["noise"] * exp(-p["decay"] * 1.4 * t)
		var v: int = int(clampf(s * p["amp"], -1.0, 1.0) * 32767.0)
		data.encode_s16(i * 2, v)
	return data


func _write_wav(path: String, pcm: PackedByteArray) -> void:
	var bytes := PackedByteArray()
	bytes.append_array("RIFF".to_ascii_buffer())
	var riff_size := PackedByteArray()
	riff_size.resize(4)
	riff_size.encode_s32(0, 36 + pcm.size())
	bytes.append_array(riff_size)
	bytes.append_array("WAVE".to_ascii_buffer())
	bytes.append_array("fmt ".to_ascii_buffer())
	var fmt_header := PackedByteArray()
	fmt_header.resize(4)
	fmt_header.encode_s32(0, 16) # fmt 块长度（不含 "fmt " 与本字段的 8 字节）
	bytes.append_array(fmt_header)
	var fmt_chunk := PackedByteArray()
	fmt_chunk.resize(16)
	fmt_chunk.encode_s16(0, 1) # PCM
	fmt_chunk.encode_s16(2, 1) # 单声道
	fmt_chunk.encode_s32(4, RATE)
	fmt_chunk.encode_s32(8, RATE * 2) # byte rate
	fmt_chunk.encode_s16(12, 2) # block align
	fmt_chunk.encode_s16(14, 16) # bits
	bytes.append_array(fmt_chunk)
	bytes.append_array("data".to_ascii_buffer())
	var data_size := PackedByteArray()
	data_size.resize(4)
	data_size.encode_s32(0, pcm.size())
	bytes.append_array(data_size)
	bytes.append_array(pcm)

	var f := FileAccess.open(path, FileAccess.WRITE)
	f.store_buffer(bytes)
	f.close()
