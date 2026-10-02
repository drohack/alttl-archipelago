"""The Thunderstore package's rules, held offline.

    py -3.13 tools/test_package_release.py

package-release.py refuses to write a Thunderstore zip that Thunderstore
would refuse (wiki: creating a package). These pin that check: the real
manifest and icon pass, and each rule fails when broken.
"""
import importlib.util
import os
import unittest

TOOLS = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location(
    "package_release", os.path.join(TOOLS, "package-release.py"))
pr = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pr)

ROOT_FILES = ["manifest.json", "icon.png", "README.md", "CHANGELOG.md"] + list(pr.PLUGIN_FILES)


def png_header(width, height):
    """Just enough PNG for the size check: the signature and an IHDR."""
    return (b"\x89PNG\r\n\x1a\n" + (13).to_bytes(4, "big") + b"IHDR"
            + width.to_bytes(4, "big") + height.to_bytes(4, "big") + b"\x08\x06\x00\x00\x00")


class TestTheThunderstorePackage(unittest.TestCase):

    def test_the_real_manifest_and_icon_pass(self):
        icon = (pr.THUNDERSTORE / "icon.png").read_bytes()
        self.assertEqual([], pr.thunderstore_problems(
            pr.thunderstore_manifest("0.4.5"), icon, ROOT_FILES))

    def test_a_description_over_250_is_refused(self):
        manifest = pr.thunderstore_manifest("0.4.5")
        manifest["description"] = "x" * 251
        problems = pr.thunderstore_problems(manifest, png_header(256, 256), ROOT_FILES)
        self.assertEqual(1, len(problems))
        self.assertIn("over 250", problems[0])

    def test_a_wrong_icon_or_a_missing_readme_is_refused(self):
        manifest = pr.thunderstore_manifest("0.4.5")
        self.assertIn("icon.png must be 256x256",
                      pr.thunderstore_problems(manifest, png_header(128, 128), ROOT_FILES))
        without = [n for n in ROOT_FILES if n != "README.md"]
        self.assertIn("README.md is missing from the zip root",
                      pr.thunderstore_problems(manifest, png_header(256, 256), without))

    def test_a_text_file_over_the_limit_is_refused(self):
        # Thunderstore's upload, 2026-10-02: "CHANGELOG.md is too long, max: 100000".
        manifest = pr.thunderstore_manifest("1.0.0")
        problems = pr.thunderstore_problems(
            manifest, png_header(256, 256), ROOT_FILES,
            texts={"README.md": "x" * 10, "CHANGELOG.md": "x" * 100_001})
        self.assertEqual(["CHANGELOG.md is 100001 characters, over 100000"], problems)

    def test_the_package_changelog_is_this_versions_section(self):
        full = (pr.ROOT / "CHANGELOG.md").read_text(encoding="utf-8")
        short = pr.thunderstore_changelog(full, "1.0.0")
        self.assertIn("\n## 1.0.0 - ", short)
        self.assertNotIn("\n## 0.4.6", short)
        self.assertIn("blob/v1.0.0/CHANGELOG.md", short)
        self.assertLess(len(short), pr.TS_TEXT_LIMIT)
        # A version the changelog has no section for is a packaging mistake.
        with self.assertRaises(ValueError):
            pr.thunderstore_changelog(full, "9.9.9")


if __name__ == "__main__":
    unittest.main(verbosity=2)
