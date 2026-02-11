@tool
extends EditorScript
## Imports spritesheets exported by the Unity SpriteAnimationExporter.
##
## Usage:
##   1. Copy your SpriteExports folder into your Godot project
##   2. Wait for Godot to import the textures
##   3. Set Project Settings > Rendering > Textures > Default Texture Filter = Nearest
##   4. Edit import_path and output_path below
##   5. Open this script in the Script Editor and run it (Script > Run)
##
## Creates a merged AnimationLibrary where each animation sets region_rect on
## each body part's Sprite2D. This means you can freely swap the texture on any
## Sprite2D to change appearance (e.g., different armor) without changing animations.
##
## Scene setup:
##   Node2D
##     +-- Body (Sprite2D, region_enabled = true)
##     +-- Hair (Sprite2D, region_enabled = true)
##     +-- Armor (Sprite2D, region_enabled = true)
##     +-- AnimationPlayer
##
## Each Sprite2D must have region_enabled = true. Set its texture to any
## exported spritesheet for that part, then play animations normally.
##
## Animation names: AnimName-Direction (e.g., "Idle01-S", "RunForward-NE")

## Path to the folder containing exported manifests (e.g., "res://SpriteExports")
var import_path := "res://SpriteExports"

## Output directory for the generated AnimationLibrary .tres file
var output_path := "res://imported_sprites"


func _run() -> void:
	var manifests := _scan_for_manifests(import_path)
	if manifests.is_empty():
		push_warning("No manifests found in: %s" % import_path)
		return

	print("Found %d manifest(s)" % manifests.size())

	var parts: Dictionary = {}
	for manifest_path in manifests:
		var part := _parse_manifest(manifest_path)
		if part.is_empty():
			continue
		var group: String = part.group_name
		if parts.has(group):
			push_warning("Duplicate group '%s', overwriting with: %s" % [group, manifest_path])
		parts[group] = part

	if parts.is_empty():
		push_error("No valid manifests parsed")
		return

	var library := _build_animation_library(parts)

	DirAccess.make_dir_recursive_absolute(output_path)
	var save_path := output_path.path_join("character_animations.tres")
	var err := ResourceSaver.save(library, save_path)
	if err != OK:
		push_error("Failed to save AnimationLibrary: %s" % save_path)
	else:
		var count := library.get_animation_list().size()
		print("Saved AnimationLibrary: %s (%d animations)" % [save_path, count])

	print("Import complete!")


# --- Scanning ---

func _scan_for_manifests(path: String) -> Array[String]:
	var result: Array[String] = []
	var dir := DirAccess.open(path)
	if dir == null:
		push_error("Cannot open directory: %s" % path)
		return result

	dir.list_dir_begin()
	var file_name := dir.get_next()
	while file_name != "":
		if dir.current_is_dir() and file_name != "." and file_name != "..":
			result.append_array(_scan_for_manifests(path.path_join(file_name)))
		elif file_name.ends_with("manifest.json"):
			result.append(path.path_join(file_name))
		file_name = dir.get_next()
	dir.list_dir_end()

	return result


# --- Manifest Parsing ---

func _parse_manifest(manifest_path: String) -> Dictionary:
	var json_text := FileAccess.get_file_as_string(manifest_path)
	if json_text.is_empty():
		push_error("Failed to read manifest: %s" % manifest_path)
		return {}

	var json := JSON.new()
	if json.parse(json_text) != OK:
		push_error("Failed to parse JSON: %s (%s)" % [manifest_path, json.get_error_message()])
		return {}

	var manifest: Dictionary = json.data
	var manifest_dir := manifest_path.get_base_dir()

	# Extract group name: prefer manifest field, fallback to folder structure
	var group_name: String = manifest.get("groupName", "")
	if group_name == "":
		group_name = manifest_dir.get_base_dir().get_file()

	var part_name: String = manifest.get("exportPrefix", "unknown")
	var pixel_size: int = int(manifest.get("pixelSize", 64))
	var max_frames_width: int = int(manifest.get("maxFramesWidth", 0))
	var sheet_width: int = int(manifest.get("sheetWidth", 0))
	var animations: Array = manifest.get("animations", [])

	print("  Parsed: %s/%s (%d animations)" % [group_name, part_name, animations.size()])

	return {
		"group_name": group_name,
		"part_name": part_name,
		"pixel_size": pixel_size,
		"animations": animations,
		"max_frames_width": max_frames_width,
		"sheet_width": sheet_width,
	}


# --- Animation Library Building ---

func _build_animation_library(parts: Dictionary) -> AnimationLibrary:
	var library := AnimationLibrary.new()

	# Collect all unique animation+direction combos across all parts
	var anim_dir_combos: Dictionary = {}

	for group_name in parts:
		var part: Dictionary = parts[group_name]
		for anim_data in part.animations:
			var anim_name: String = anim_data.get("name", "")
			var fps: int = int(anim_data.get("fps", 10))
			var frames_per_dir: int = int(anim_data.get("framesPerDirection", 1))
			var dirs: Array = anim_data.get("directions", [])

			for dir_name in dirs:
				var key := "%s-%s" % [anim_name, dir_name]
				if not anim_dir_combos.has(key):
					anim_dir_combos[key] = {
						"anim_name": anim_name,
						"direction": dir_name,
						"fps": fps,
						"frames_per_dir": frames_per_dir
					}

	# Create an Animation for each combo
	for key in anim_dir_combos:
		var info: Dictionary = anim_dir_combos[key]
		var fps: int = info.fps
		var frame_count: int = info.frames_per_dir

		var anim := Animation.new()
		anim.length = float(frame_count) / float(fps)
		anim.loop_mode = Animation.LOOP_LINEAR

		# Add a region_rect track for each part that has this animation+direction
		for group_name in parts:
			var part: Dictionary = parts[group_name]

			var anim_data := _find_animation(part.animations, info.anim_name)
			if anim_data.is_empty():
				continue

			var dirs: Array = anim_data.get("directions", [])
			var dir_index := dirs.find(info.direction)
			if dir_index < 0:
				continue

			var max_frames: int = part.max_frames_width
			var rows_per_dir: int = maxi(int(anim_data.get("rowsPerDirection", 1)), 1)
			var row_start: int = int(anim_data.get("rowStart", 0))

			# Add value track targeting {GroupName}:region_rect
			var track_idx := anim.add_track(Animation.TYPE_VALUE)
			anim.track_set_path(track_idx, "%s:region_rect" % group_name)
			anim.value_track_set_update_mode(track_idx, Animation.UPDATE_DISCRETE)

			# Insert keyframes — each sets region_rect to the correct area
			var part_frame_count: int = int(anim_data.get("framesPerDirection", frame_count))
			for f in range(mini(frame_count, part_frame_count)):
				var rect := _get_frame_rect(
					part.pixel_size, row_start, rows_per_dir,
					max_frames, dir_index, f
				)
				anim.track_insert_key(track_idx, float(f) / float(fps), rect)

		library.add_animation(key, anim)

	return library


# --- Helpers ---

func _find_animation(animations: Array, anim_name: String) -> Dictionary:
	for anim in animations:
		if anim.get("name", "") == anim_name:
			return anim
	return {}


func _get_frame_rect(
	pixel_size: int,
	row_start: int,
	rows_per_dir: int,
	max_frames: int,
	dir_index: int,
	frame_index: int
) -> Rect2:
	var col: int = frame_index % max_frames if max_frames > 0 else frame_index
	var row_within_dir: int = frame_index / max_frames if max_frames > 0 else 0
	var row: int = row_start + dir_index * rows_per_dir + row_within_dir

	return Rect2(
		col * pixel_size,
		row * pixel_size,
		pixel_size,
		pixel_size
	)
