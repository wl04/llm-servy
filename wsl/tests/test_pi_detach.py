import importlib.util
from pathlib import Path
import subprocess
import unittest
from unittest.mock import Mock, patch

SCRIPT = Path(__file__).resolve().parents[1] / 'pi-supervisor.py'
spec = importlib.util.spec_from_file_location('pi_supervisor', SCRIPT)
supervisor = importlib.util.module_from_spec(spec)
spec.loader.exec_module(supervisor)


class PiDetachTests(unittest.TestCase):
    def test_detach_waits_for_acknowledgement_when_command_returns_before_client_leaves(self):
        command = Mock(side_effect=[
            subprocess.CompletedProcess([], 0, ''),
            subprocess.CompletedProcess([], 0, '/dev/pts/42\n'),
            subprocess.CompletedProcess([], 0, '/dev/pts/42\n'),
            subprocess.CompletedProcess([], 0, ''),
        ])

        with patch.object(supervisor.time, 'sleep') as wait:
            supervisor.detach_clients(command)

        self.assertEqual(command.call_count, 4)
        self.assertEqual(wait.call_count, 2)

    def test_detach_reports_timeout_when_client_never_acknowledges(self):
        now = [0.0]
        command = Mock(return_value=subprocess.CompletedProcess([], 0, '/dev/pts/42\n'))
        def advance(seconds):
            now[0] += seconds

        with patch.object(supervisor.time, 'monotonic', side_effect=lambda: now[0]), \
                patch.object(supervisor.time, 'sleep', side_effect=advance):
            with self.assertRaises(TimeoutError):
                supervisor.detach_clients(command, timeout=.1)

    def test_detach_reports_failure_when_client_query_fails(self):
        command = Mock(side_effect=[
            subprocess.CompletedProcess([], 0, ''),
            subprocess.CompletedProcess([], 1, ''),
        ])

        with self.assertRaisesRegex(RuntimeError, 'inspect'):
            supervisor.detach_clients(command)
