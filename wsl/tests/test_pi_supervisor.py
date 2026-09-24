import json
import os
import pty
import time
from pathlib import Path
import selectors
import shutil
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'pi-supervisor.py'


@unittest.skipUnless(shutil.which('tmux'), 'tmux is required')
class PiSupervisorTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.pi = self.root / 'fake pi'
        self.pi.write_text('#!' + sys.executable + '\nimport time\ntime.sleep(60)\n')
        self.pi.chmod(0o755)
        self.process = None

    def tearDown(self):
        if self.process:
            if self.process.poll() is None:
                self.process.communicate('stop\n', timeout=10)
            for stream in (self.process.stdin, self.process.stdout, self.process.stderr):
                stream.close()
        self.temp.cleanup()

    def launch(self, executable=None):
        self.process = subprocess.Popen([sys.executable, str(SCRIPT), '--executable', str(executable or self.pi),
            '--provider', 'local', '--model', 'model with spaces'], cwd=self.root,
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        return self.process

    def event(self):
        with selectors.DefaultSelector() as selector:
            selector.register(self.process.stdout, selectors.EVENT_READ)
            self.assertTrue(selector.select(8), 'No supervisor event before timeout')
        line = self.process.stdout.readline()
        self.assertTrue(line, 'Supervisor exited without an event')
        return json.loads(line)

    def test_stop_removes_owned_session_when_terminal_is_detached(self):
        process = self.launch()
        event = self.event()
        self.assertEqual(event['state'], 'running')
        socket = event['socket']
        check = subprocess.run(['tmux', '-S', socket, 'display-message', '-p', '-t', 'pi', '#{session_attached}'], capture_output=True, text=True)
        self.assertEqual(check.stdout.strip(), '0')
        self.assertIsNone(process.poll())

        process.communicate('stop\n', timeout=10)

        self.assertEqual(process.returncode, 0)
        self.assertFalse(Path(socket).exists())

    def test_exit_reports_failure_when_pi_exits_with_error(self):
        self.pi.write_text('#!' + sys.executable + '\nimport sys\nsys.exit(17)\n')
        process = self.launch()
        process.wait(timeout=10)
        events = process.stdout.read()
        decoded = [json.loads(line) for line in events.splitlines()]

        self.assertIn({'state': 'exited', 'exitCode': 17}, decoded)
        self.assertNotEqual(process.returncode, 0)

    def test_disconnect_stops_session_when_controller_closes_input(self):
        process = self.launch()
        event = self.event()
        process.stdin.close()
        process.wait(timeout=10)

        self.assertEqual(process.returncode, 0)
        self.assertFalse(Path(event['socket']).exists())

    def test_exit_reports_success_when_pi_exits_normally(self):
        self.pi.write_text('#!' + sys.executable + '\n')
        process = self.launch()
        process.wait(timeout=10)

        events = [json.loads(line) for line in process.stdout.read().splitlines()]
        self.assertIn({'state': 'exited', 'exitCode': 0}, events)
        self.assertEqual(process.returncode, 0)

    def test_start_reports_missing_executable_when_pi_not_installed(self):
        process = self.launch(self.root / 'missing')
        process.wait(timeout=10)

        self.assertEqual(self.event(), {'state': 'error', 'code': 'PiMissingExecutable'})
        self.assertEqual(process.returncode, 1)

    def test_stop_preserves_independent_session_when_another_tmux_server_exists(self):
        other_socket = str(self.root / 'other.sock')
        subprocess.run(['tmux', '-S', other_socket, '-f', '/dev/null', 'new-session', '-d', '-s', 'other', 'sleep 60'], check=True)
        try:
            process = self.launch()
            self.assertEqual(self.event()['state'], 'running')

            process.communicate('stop\n', timeout=10)

            check = subprocess.run(['tmux', '-S', other_socket, 'has-session', '-t', 'other'])
            self.assertEqual(check.returncode, 0)
        finally:
            subprocess.run(['tmux', '-S', other_socket, 'kill-server'], check=True)

    def test_attach_reuses_process_when_terminal_is_reopened(self):
        process = self.launch()
        socket = self.event()['socket']
        def pane_pid():
            return subprocess.check_output(['tmux', '-S', socket, 'display-message', '-p', '-t', 'pi', '#{pane_pid}'], text=True).strip()
        original_pid = pane_pid()
        for _ in range(2):
            master, slave = pty.openpty()
            viewer = subprocess.Popen(['tmux', '-S', socket, 'attach-session', '-t', 'pi'],
                stdin=slave, stdout=slave, stderr=slave, env=dict(os.environ, TERM='xterm-256color'))
            os.close(slave)
            try:
                deadline = time.monotonic() + 5
                while time.monotonic() < deadline:
                    attached = subprocess.check_output(['tmux', '-S', socket, 'display-message', '-p', '-t', 'pi', '#{session_attached}'], text=True).strip()
                    if attached == '1':
                        break
                    time.sleep(.05)
                self.assertEqual(attached, '1')
                self.assertEqual(pane_pid(), original_pid)
            finally:
                os.close(master)
                viewer.wait(timeout=5)
            self.assertIsNone(process.poll())
        process.communicate('stop\n', timeout=10)

    def test_start_preserves_arguments_when_executable_path_contains_spaces(self):
        self.pi.write_text('#!' + sys.executable + '\nimport json,sys,time\nfrom pathlib import Path\nPath("arguments.json").write_text(json.dumps(sys.argv[1:]))\ntime.sleep(60)\n')
        process = self.launch()
        self.assertEqual(self.event()['state'], 'running')
        deadline = time.monotonic() + 5
        arguments = self.root / 'arguments.json'
        while not arguments.exists() and time.monotonic() < deadline:
            time.sleep(.05)

        self.assertEqual(json.loads(arguments.read_text()), ['--provider', 'local', '--model', 'model with spaces'])
        process.communicate('stop\n', timeout=10)
