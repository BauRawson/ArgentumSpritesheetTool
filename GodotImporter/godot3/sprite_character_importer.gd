tool
extends EditorScript
# Imports spritesheets exported by the Unity SpriteAnimationExporter.
#
# Usage:
#   1. Copy your SpriteExports folder into your Godot project
#   2. Wait for Godot to import the textures
#   3. Set Project Settings > Rendering > Quality > 2D > Use Pixel Snap = On
#   4. Edit import_path and output_path below
#   5. Open this script in the Script Editor and run it (Script > Run)
#
# Since Godot 3.x has no AnimationLibrary, this creates individual Animation
# .tres files. Each animation sets region_rect on each body part's Sprite, so
# you can freely swap textures without changing animations.
#
# Scene setup:
#   Node2D
#     +-- Body (Sprite, region_enabled = true)
#     +-- Hair (Sprite, region_enabled = true)
#     +-- Armor (Sprite, region_enabled = true)
#     +-- AnimationPlayer
#
# Each Sprite must have region_enabled = true. Set its texture to any
# exported spritesheet for that part, then play animations normally.
#
# Animation files: AnimName-Direction.tres (e.g., "Idle01-S.tres")

# Path to the folder containing exported manifests (e.g., "res://SpriteExports")
var import_path = "res://SpriteExports"

# Output directory for generated Animation .tres files
var output_path = "res://imported_sprites"


func _run():
	var manifests = _scan_for_manifests(import_path)
	if manifests.empty():
		push_warning("No manifests found in: %s" % import_path)
		return

	print("Found %d manifest(s)" % manifests.size())

	var parts = {}
	for manifest_path in manifests:
		var part = _parse_manifest(manifest_path)
		if part.empty():
			continue
		var group = part.group_name
		if parts.has(group):
			push_warning("Duplicate group '%s', overwriting with: %s" % [group, manifest_path])
		parts[group] = part

	if parts.empty():
		push_error("No valid manifests parsed")
		return

	var count = _build_and_save_animations(parts)
	print("Import complete! Saved %d animations to %s" % [count, output_path])


# --- Scanning ---

func _scan_for_manifests(path):
	var result = []
	var dir = Directory.new()
	if dir.open(path) != OK:
		push_error("Cannot open directory: %s" % path)
		return result

	dir.list_dir_begin(true, false)
	var file_name = dir.get_next()
	while file_name != "":
		var full_path = path.plus_file(file_name)
		if dir.current_is_dir():
			result.append_array(_scan_for_manifests(full_path))
		elif file_name.ends_with("manifest.json"):
			result.append(full_path)
		file_name = dir.get_next()
	dir.list_dir_end()

	return result


# --- Manifest Parsing ---

func _parse_manifest(manifest_path):
	var file = File.new()
	if file.open(manifest_path, File.READ) != OK:
		push_error("Failed to read manifest: %s" % manifest_path)
		return {}
	var json_text = file.get_as_text()
	file.close()

	var parse_result = JSON.parse(json_text)
	if parse_result.error != OK:
		push_error("Failed to parse JSON: %s (%s)" % [manifest_path, parse_result.error_string])
		return {}

	var manifest = parse_result.result
	var manifest_dir = manifest_path.get_base_dir()

	# Extract group name: prefer manifest field, fallback to folder structure
	var group_name = manifest.get("groupName", "")
	if group_name == "":
		group_name = manifest_dir.get_base_dir().get_file()

	var part_name = manifest.get("exportPrefix", "unknown")
	var pixel_size = int(manifest.get("pixelSize", 64))
	var max_frames_width = int(manifest.get("maxFramesWidth", 0))
	var sheet_width = int(manifest.get("sheetWidth", 0))
	var animations = manifest.get("animations", [])

	# Handle null values from JSON
	if max_frames_width == null:
		max_frames_width = 0
	if sheet_width == null:
		sheet_width = 0

	print("  Parsed: %s/%s (%d animations)" % [group_name, part_name, animations.size()])

	return {
		"group_name": group_name,
		"part_name": part_name,
		"pixel_size": pixel_size,
		"animations": animations,
		"max_frames_width": max_frames_width,
		"sheet_width": sheet_width,
	}


# --- Animation Building ---

func _build_and_save_animations(parts):
	var dir = Directory.new()
	dir.make_dir_recursive(output_path)

	# Collect all unique animation+direction combos across all parts
	var anim_dir_combos = {}

	for group_name in parts:
		var part = parts[group_name]
		for anim_data in part.animations:
			var anim_name = anim_data.get("name", "")
			var fps = int(anim_data.get("fps", 10))
			var frames_per_dir = int(anim_data.get("framesPerDirection", 1))
			var dirs = anim_data.get("directions", [])

			for dir_name in dirs:
				var key = "%s-%s" % [anim_name, dir_name]
				if not anim_dir_combos.has(key):
					anim_dir_combos[key] = {
						"anim_name": anim_name,
						"direction": dir_name,
						"fps": fps,
						"frames_per_dir": frames_per_dir
					}

	# Create and save an Animation for each combo
	var count = 0
	for key in anim_dir_combos:
		var info = anim_dir_combos[key]
		var fps = info.fps
		var frame_count = info.frames_per_dir

		var anim = Animation.new()
		anim.length = float(frame_count) / float(fps)
		anim.loop = true

		# Add a region_rect track for each part that has this animation+direction
		for group_name in parts:
			var part = parts[group_name]

			var anim_data = _find_animation(part.animations, info.anim_name)
			if anim_data.empty():
				continue

			var dirs = anim_data.get("directions", [])
			var dir_index = dirs.find(info.direction)
			if dir_index < 0:
				continue

			var max_frames = part.max_frames_width
			var rows_per_dir = int(max(int(anim_data.get("rowsPerDirection", 1)), 1))
			var row_start = int(anim_data.get("rowStart", 0))

			# Add value track targeting {GroupName}:region_rect
			var track_idx = anim.add_track(Animation.TYPE_VALUE)
			anim.track_set_path(track_idx, "%s:region_rect" % group_name)
			anim.value_track_set_update_mode(track_idx, Animation.UPDATE_DISCRETE)

			# Insert keyframes — each sets region_rect to the correct area
			var part_frame_count = int(anim_data.get("framesPerDirection", frame_count))
			for f in range(min(frame_count, part_frame_count)):
				var rect = _get_frame_rect(
					part.pixel_size, row_start, rows_per_dir,
					max_frames, dir_index, f
				)
				anim.track_insert_key(track_idx, float(f) / float(fps), rect)

		# Save individual Animation .tres file
		var save_path = output_path.plus_file("%s.tres" % key)
		var err = ResourceSaver.save(save_path, anim)
		if err != OK:
			push_warning("Failed to save animation: %s" % save_path)
		else:
			count += 1

	return count


# --- Helpers ---

func _find_animation(animations, anim_name):
	for anim in animations:
		if anim.get("name", "") == anim_name:
			return anim
	return {}


func _get_frame_rect(pixel_size, row_start, rows_per_dir, max_frames, dir_index, frame_index):
	var col = frame_index % max_frames if max_frames > 0 else frame_index
	# warning-ignore:INTEGER_DIVISION
	var row_within_dir = frame_index / max_frames if max_frames > 0 else 0
	var row = row_start + dir_index * rows_per_dir + row_within_dir

	return Rect2(
		col * pixel_size,
		row * pixel_size,
		pixel_size,
		pixel_size
	)
