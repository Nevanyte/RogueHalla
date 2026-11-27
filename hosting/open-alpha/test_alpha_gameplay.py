import importlib.util
import pathlib
import tempfile
import unittest


SCRIPT = pathlib.Path(__file__).with_name("alpha-gameplay.py")
SPEC = importlib.util.spec_from_file_location("alpha_gameplay", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class GameplayCommandTest(unittest.TestCase):
    def test_replaces_only_gameplay_settings(self):
        with tempfile.TemporaryDirectory() as directory:
            binary = pathlib.Path(directory) / "rh-server"
            binary.write_text("#!/bin/sh\n")
            binary.chmod(0o755)
            old = [
                str(binary), "--runtime", "source-arena", "--map", "/private/map.json",
                "--fill-bots", "4", "--min-players", "2", "--database-url",
                "postgresql:///private", "--control-token-file", "/private/token",
                "--bot-learning", "off", "--ws-origins", "https://example.test",
            ]
            command = MODULE.gameplay_argv(old)
            self.assertEqual(command[:5], old[:5])
            self.assertEqual(command[-4:], ["--fill-bots", "8", "--bot-brain", "raid"])
            for flag in ("--min-players", "--database-url", "--control-token-file",
                         "--bot-learning", "--ws-origins"):
                position = old.index(flag)
                self.assertEqual(command[command.index(flag) + 1], old[position + 1])
            self.assertEqual(command.count("--map-pool"), 1)
            self.assertEqual(command.count("--signature-landmarks"), 1)

    def test_refuses_non_source_and_duplicate_fill(self):
        with tempfile.TemporaryDirectory() as directory:
            binary = pathlib.Path(directory) / "rh-server"
            binary.write_text("#!/bin/sh\n")
            binary.chmod(0o755)
            with self.assertRaises(ValueError):
                MODULE.gameplay_argv([str(binary), "--runtime", "training", "--map", "x"])
            with self.assertRaises(ValueError):
                MODULE.gameplay_argv([str(binary), "--runtime", "source-arena", "--map", "x",
                                      "--fill-bots", "4", "--fill-bots", "8"])


if __name__ == "__main__":
    unittest.main()
