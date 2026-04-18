extends SceneTree

const MOD_ID := "CampfireBugFix"
const MANIFEST_PATH := "res://CampfireBugFix.json"
const OUTPUT_DIR := "res://build"
const OUTPUT_FILE := "res://build/CampfireBugFix.pck"
const EXTERNAL_ASSET_DIR_NAME := "CampfireBugFix"
const MOD_IMAGE_NAME := "mod_image.png"


func _initialize() -> void:
	var project_dir := ProjectSettings.globalize_path("res://")
	var external_asset_dir := project_dir.path_join(EXTERNAL_ASSET_DIR_NAME)

	var err := DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUTPUT_DIR))
	if err != OK:
		push_error("Failed to create output dir: %s" % err)
		quit_with_error()

	var packer := PCKPacker.new()
	err = packer.pck_start(OUTPUT_FILE)
	if err != OK:
		push_error("pck_start failed: %s" % err)
		quit_with_error()

	# 1) 把 manifest 自己打进去
	err = add_manifest(packer)
	if err != OK:
		push_error("add_manifest failed: %s" % err)
		quit_with_error()

	# 2) 把同名资源目录整个打进去
	err = add_external_dir(packer, external_asset_dir, "res://%s" % EXTERNAL_ASSET_DIR_NAME)
	if err != OK:
		push_error("add_external_dir failed: %s" % err)
		quit_with_error()

	# 3) 给 mod_image.png 补导入链（.import / .ctex / .md5）
	var image_source := external_asset_dir.path_join(MOD_IMAGE_NAME)
	if FileAccess.file_exists(image_source):
		err = add_mod_image_import_chain(
			packer,
			project_dir,
			"res://%s/%s" % [EXTERNAL_ASSET_DIR_NAME, MOD_IMAGE_NAME]
		)
		if err != OK:
			push_error("add_mod_image_import_chain failed: %s" % err)
			quit_with_error()
	else:
		print("No %s found, skipping import-chain packaging." % image_source)

	err = packer.flush()
	if err != OK:
		push_error("flush failed: %s" % err)
		quit_with_error()

	print("PCK built successfully: %s" % OUTPUT_FILE)
	quit(0)


func add_manifest(packer: PCKPacker) -> int:
	if not FileAccess.file_exists(MANIFEST_PATH):
		push_error("Manifest not found: %s" % MANIFEST_PATH)
		return ERR_FILE_NOT_FOUND

	return packer.add_file(MANIFEST_PATH, ProjectSettings.globalize_path(MANIFEST_PATH))


func add_external_dir(packer: PCKPacker, source_dir: String, target_dir: String) -> int:
	var dir := DirAccess.open(source_dir)
	if dir == null:
		push_error("Cannot open asset dir: %s" % source_dir)
		return ERR_CANT_OPEN

	for file_name in dir.get_files():
		var source_file := source_dir.path_join(file_name)
		var target_file := target_dir.path_join(file_name)

		var err := packer.add_file(target_file, source_file)
		if err != OK:
			push_error("Failed to add file: %s -> %s (%s)" % [source_file, target_file, err])
			return err

	for subdir_name in dir.get_directories():
		var err := add_external_dir(
			packer,
			source_dir.path_join(subdir_name),
			target_dir.path_join(subdir_name)
		)
		if err != OK:
			return err

	return OK


func add_mod_image_import_chain(packer: PCKPacker, project_dir: String, image_target_path: String) -> int:
	var imported_dir := project_dir.path_join(".godot/imported")
	var ctex_name := find_mod_image_ctex(imported_dir)
	if ctex_name.is_empty():
		push_warning("No imported ctex found for %s. Open the project once in Godot first." % MOD_IMAGE_NAME)
		return OK

	var ctex_source := imported_dir.path_join(ctex_name)
	var ctex_target := "res://.godot/imported/%s" % ctex_name

	var err := packer.add_file(ctex_target, ctex_source)
	if err != OK:
		push_error("Failed to add ctex: %s -> %s (%s)" % [ctex_source, ctex_target, err])
		return err

	var md5_name := "%s.md5" % ctex_name
	var md5_source := imported_dir.path_join(md5_name)
	if FileAccess.file_exists(md5_source):
		err = packer.add_file("res://.godot/imported/%s" % md5_name, md5_source)
		if err != OK:
			push_error("Failed to add md5: %s (%s)" % [md5_source, err])
			return err

	var temp_import_local := "user://mod_image_runtime.import"
	var temp_import_global := ProjectSettings.globalize_path(temp_import_local)

	var import_file := FileAccess.open(temp_import_local, FileAccess.WRITE)
	if import_file == null:
		push_error("Failed to create temp import file.")
		return ERR_CANT_CREATE

	import_file.store_string(build_import_content(image_target_path, ctex_target))
	import_file.close()

	err = packer.add_file("%s.import" % image_target_path, temp_import_global)
	if err != OK:
		push_error("Failed to add .import file: %s (%s)" % [image_target_path, err])
		return err

	DirAccess.remove_absolute(temp_import_global)
	return OK


func find_mod_image_ctex(imported_dir: String) -> String:
	var dir := DirAccess.open(imported_dir)
	if dir == null:
		return ""

	for file_name in dir.get_files():
		if file_name.begins_with("%s-" % MOD_IMAGE_NAME) and file_name.ends_with(".ctex"):
			return file_name

	return ""


func build_import_content(source_file: String, ctex_target: String) -> String:
	return """[remap]

importer="texture"
type="CompressedTexture2D"
path="%s"
metadata={
"vram_texture": false
}

[deps]

source_file="%s"
dest_files=["%s"]

[params]

compress/mode=0
compress/high_quality=false
compress/lossy_quality=0.7
compress/uastc_level=0
compress/rdo_quality_loss=0.0
compress/hdr_compression=1
compress/normal_map=0
compress/channel_pack=0
mipmaps/generate=false
mipmaps/limit=-1
roughness/mode=0
roughness/src_normal=""
process/channel_remap/red=0
process/channel_remap/green=1
process/channel_remap/blue=2
process/channel_remap/alpha=3
process/fix_alpha_border=true
process/premult_alpha=false
process/normal_map_invert_y=false
process/hdr_as_srgb=false
process/hdr_clamp_exposure=false
process/size_limit=0
detect_3d/compress_to=1
""" % [ctex_target, source_file, ctex_target]


func quit_with_error() -> void:
	quit(1)