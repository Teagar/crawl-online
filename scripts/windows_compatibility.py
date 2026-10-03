"""Windows x86/.NET 3.5 compatibility checks shared by release tooling."""

from __future__ import annotations

import json
import struct
from pathlib import Path
import xml.etree.ElementTree as ET


class CompatibilityError(ValueError):
    pass


def _u16(data: bytes, offset: int) -> int:
    if offset < 0 or offset + 2 > len(data):
        raise CompatibilityError("truncated 16-bit field")
    return struct.unpack_from("<H", data, offset)[0]


def _u32(data: bytes, offset: int) -> int:
    if offset < 0 or offset + 4 > len(data):
        raise CompatibilityError("truncated 32-bit field")
    return struct.unpack_from("<I", data, offset)[0]


def inspect_managed_pe(data: bytes, name: str = "assembly") -> dict[str, object]:
    try:
        if len(data) < 64 or data[:2] != b"MZ":
            raise CompatibilityError("not a PE file")
        pe = _u32(data, 0x3C)
        if pe < 64 or pe + 24 > len(data) or data[pe:pe + 4] != b"PE\0\0":
            raise CompatibilityError("invalid PE signature or offset")
        machine = _u16(data, pe + 4)
        if machine != 0x014C:
            raise CompatibilityError(f"PE machine 0x{machine:04x} is not Windows i386")
        section_count = _u16(data, pe + 6)
        optional_size = _u16(data, pe + 20)
        optional = pe + 24
        if optional + optional_size > len(data) or optional_size < 224:
            raise CompatibilityError("truncated PE32 optional header")
        magic = _u16(data, optional)
        if magic != 0x010B:
            raise CompatibilityError(f"optional-header magic 0x{magic:04x} is not PE32")
        if _u32(data, optional + 92) < 15:
            raise CompatibilityError("PE has no CLR data-directory slot")
        clr_rva = _u32(data, optional + 96 + 14 * 8)
        clr_size = _u32(data, optional + 96 + 14 * 8 + 4)
        if clr_rva == 0 or clr_size < 20:
            raise CompatibilityError("missing CLR header")

        sections = optional + optional_size
        if section_count < 1 or sections + section_count * 40 > len(data):
            raise CompatibilityError("invalid PE section table")

        def rva_to_offset(rva: int, size: int) -> int:
            for index in range(section_count):
                section = sections + index * 40
                virtual_size = _u32(data, section + 8)
                virtual_address = _u32(data, section + 12)
                raw_size = _u32(data, section + 16)
                raw_offset = _u32(data, section + 20)
                extent = max(virtual_size, raw_size)
                if virtual_address <= rva and rva + size <= virtual_address + extent:
                    result = raw_offset + rva - virtual_address
                    if result + size > len(data):
                        break
                    return result
            raise CompatibilityError(f"RVA 0x{rva:x} is outside file-backed sections")

        clr = rva_to_offset(clr_rva, 20)
        if _u32(data, clr) < 20:
            raise CompatibilityError("invalid CLR header size")
        flags = _u32(data, clr + 16)
        if not flags & 0x1:
            raise CompatibilityError("CLR image is not IL-only")
        if flags & 0x10:
            raise CompatibilityError("CLR image has a native entrypoint")
        metadata_rva = _u32(data, clr + 8)
        metadata_size = _u32(data, clr + 12)
        if metadata_size < 20:
            raise CompatibilityError("missing CLR metadata")
        metadata = rva_to_offset(metadata_rva, min(metadata_size, 32))
        if _u32(data, metadata) != 0x424A5342:
            raise CompatibilityError("invalid CLR metadata signature")
        version_length = _u32(data, metadata + 12)
        if version_length < 1 or version_length > 128:
            raise CompatibilityError("invalid CLR metadata version length")
        version_offset = rva_to_offset(metadata_rva + 16, version_length)
        runtime = data[version_offset:version_offset + version_length].split(b"\0", 1)[0].decode("ascii", "strict")
        if runtime != "v2.0.50727":
            raise CompatibilityError(f"CLR runtime {runtime!r} is not the .NET 2.0-3.5 runtime")
        return {
            "name": name,
            "machine": "i386",
            "format": "PE32",
            "ilOnly": True,
            "requires32Bit": bool(flags & 0x2),
            "runtime": runtime,
        }
    except (IndexError, struct.error, UnicodeError) as error:
        raise CompatibilityError(f"malformed PE/CLR data: {error}") from error


def audit_project(project_path: Path) -> dict[str, object]:
    project_path = project_path.resolve()
    root = ET.parse(project_path).getroot()

    def values(tag: str) -> list[str]:
        return [(element.text or "").strip() for element in root.iter(tag)]

    frameworks = values("TargetFramework") + values("TargetFrameworks")
    if frameworks != ["net35"]:
        raise CompatibilityError(f"{project_path.name}: target framework must be exactly net35")
    forbidden_properties = ("RuntimeIdentifier", "RuntimeIdentifiers", "SelfContained")
    for property_name in forbidden_properties:
        if any(value for value in values(property_name)):
            raise CompatibilityError(f"{project_path.name}: {property_name} is not allowed")

    allowed_packages = {"Microsoft.NETFramework.ReferenceAssemblies.net35"}
    package_names = {element.attrib.get("Include", "") for element in root.iter("PackageReference")}
    if package_names != allowed_packages:
        raise CompatibilityError(f"{project_path.name}: unexpected PackageReference set {sorted(package_names)}")
    for element in root.iter("PackageReference"):
        private = element.attrib.get("PrivateAssets", "") or next(
            (child.text or "" for child in element if child.tag == "PrivateAssets"), "")
        if private.strip().lower() != "all":
            raise CompatibilityError(f"{project_path.name}: build-only package must use PrivateAssets=all")

    references = []
    for element in root.iter("Reference"):
        include = element.attrib.get("Include", "")
        hint = next((child.text or "" for child in element if child.tag == "HintPath"), "")
        private = next((child.text or "" for child in element if child.tag == "Private"), "")
        if not hint.startswith(("$(BepInExRoot)", "$(CrawlGameDir)")):
            raise CompatibilityError(f"{project_path.name}: reference {include} is not rooted externally")
        if private.strip().lower() != "false":
            raise CompatibilityError(f"{project_path.name}: reference {include} must not be copied into the package")
        references.append(include)

    assets_path = project_path.parent / "obj" / "project.assets.json"
    if not assets_path.is_file():
        raise CompatibilityError(f"{project_path.name}: restore assets are missing; run dotnet restore")
    assets = json.loads(assets_path.read_text(encoding="utf-8"))
    if set(assets.get("targets", {})) != {".NETFramework,Version=v3.5"}:
        raise CompatibilityError(f"{project_path.name}: restore target is not exclusively .NET Framework 3.5")
    allowed_libraries = {
        "Microsoft.NETFramework.ReferenceAssemblies/1.0.3",
        "Microsoft.NETFramework.ReferenceAssemblies.net35/1.0.3",
    }
    libraries = set(assets.get("libraries", {}))
    if libraries != allowed_libraries:
        raise CompatibilityError(f"{project_path.name}: unexpected restored libraries {sorted(libraries - allowed_libraries)}")
    return {"project": project_path.name, "target": "net35", "externalReferences": sorted(references)}
