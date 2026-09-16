import json
import os
from pathlib import Path
import signal
import socket
import subprocess
import sys
import tempfile
import time
import unittest

SUPERVISOR = Path(__file__).resolve().parents[1] / 'wsl-supervisor.py'


class SupervisorTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.record = self.root / 'started.json'
        npx = self.root / 'npx'
        npx.write_text('#!' + sys.executable + '\n' + '''
import json,os,subprocess,sys,time
child = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'])
with open(os.environ['TEST_RECORD'], 'w') as f:
    json.dump({'pid': os.getpid(), 'child': child.pid, 'args': sys.argv[1:]}, f)
if os.environ.get('TEST_EXIT'):
    sys.exit(17)
time.sleep(60)
''')
        npx.chmod(0o755)
        self.env = dict(os.environ, PATH=str(self.root), TMPDIR=str(self.root), TEST_RECORD=str(self.record), npm_config_cache=str(self.root/'cache'))
        with socket.socket() as s:
            s.bind(('127.0.0.1', 0))
            self.port = s.getsockname()[1]
        self.procs = []

    def tearDown(self):
        for p in self.procs:
            if p.poll() is None:
                p.terminate()
                p.wait(timeout=8)
            if p.stdin and not p.stdin.closed:
                p.stdin.close()
            p.stdout.close()
            p.stderr.close()
        self.temp.cleanup()

    def launch(self):
        p = subprocess.Popen([sys.executable, str(SUPERVISOR), '--port', str(self.port)],
                             env=self.env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        self.procs.append(p)
        return p

    def started(self):
        end = time.monotonic() + 5
        while time.monotonic() < end:
            try:
                return json.loads(self.record.read_text())
            except (FileNotFoundError, json.JSONDecodeError):
                time.sleep(.05)
        self.fail('DSH did not start')

    def assert_stopped(self, pid):
        path = Path('/proc') / str(pid) / 'stat'
        self.assertTrue(not path.exists() or path.read_text().split(') ')[1].startswith('Z'), str(pid))

    def test_stop_terminates_owned_group_when_independent_process_exists(self):
        unrelated = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'])
        try:
            p = self.launch(); info = self.started()
            self.assertEqual(info['args'], ['--yes', '--loglevel', 'info', '@deepseek-ai/dsh@0.1.1-rc.2', 'web', '--host',
                                           '127.0.0.1', '--port', str(self.port), '--no-open'])
            p.stdin.write(b'stop\n'); p.stdin.flush()
            self.assertEqual(p.wait(timeout=8), 0)
            self.assert_stopped(info['pid']); self.assert_stopped(info['child'])
            self.assertIsNone(unrelated.poll())
        finally:
            unrelated.terminate(); unrelated.wait()

    def test_disconnect_stops_group_when_controller_closes_pipe(self):
        p = self.launch(); info = self.started()
        p.stdin.close()
        self.assertEqual(p.wait(timeout=8), 0)
        self.assert_stopped(info['child'])

    def test_start_rejects_duplicate_when_port_lock_held(self):
        p = self.launch(); self.started()
        other = self.launch()
        self.assertEqual(other.wait(timeout=5), 1)
        self.assertIn(b'already owns', other.stderr.read())
        self.assertIsNone(p.poll())

    def test_start_rejects_port_when_another_server_listening(self):
        with socket.socket() as s:
            s.bind(('127.0.0.1', self.port)); s.listen()
            p = self.launch()
            self.assertEqual(p.wait(timeout=5), 1)
            self.assertIn(b'port is occupied', p.stderr.read())
            self.assertFalse(self.record.exists())

    def test_start_accepts_port_when_previous_connection_is_in_time_wait(self):
        with socket.socket() as server, socket.socket() as client:
            server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            server.bind(('127.0.0.1', self.port))
            server.listen()
            client.connect(('127.0.0.1', self.port))
            connection, _ = server.accept()
            connection.close()
            self.assertEqual(client.recv(1), b'')

        p = self.launch()
        self.started()
        p.stdin.write(b'stop\n'); p.stdin.flush()
        self.assertEqual(p.wait(timeout=8), 0)

    def test_exit_cleans_descendants_when_leader_fails(self):
        self.env['TEST_EXIT'] = '1'
        p = self.launch(); info = self.started()
        self.assertEqual(p.wait(timeout=8), 17)
        self.assert_stopped(info['child'])

class InitializationTests(unittest.TestCase):
    def test_start_cleans_child_when_selector_registration_fails(self):
        import importlib.util
        from unittest.mock import patch, MagicMock
        spec = importlib.util.spec_from_file_location('supervisor_under_test', SUPERVISOR)
        supervisor = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(supervisor)
        child = MagicMock(pid=123456)
        selector = MagicMock()
        selector.register.side_effect = OSError('registration failed')
        with tempfile.TemporaryDirectory() as directory:
            with patch.object(sys, 'argv', ['supervisor', '--port', '13083']), \
                 patch.object(supervisor, 'cached_cli', return_value=supervisor.CachedCli(Path('/test/dsh.js'), ())), \
                 patch.object(supervisor.shutil, 'which', return_value='/test/node'), \
                 patch.object(supervisor.tempfile, 'gettempdir', return_value=directory), \
                 patch.object(supervisor.socket, 'socket'), \
                 patch.object(supervisor.signal, 'signal'), \
                 patch.object(supervisor.subprocess, 'Popen', return_value=child), \
                 patch.object(supervisor.selectors, 'DefaultSelector', return_value=selector), \
                 patch.object(supervisor.os, 'killpg') as kill_group, \
                 patch.object(supervisor.time, 'sleep'):
                with self.assertRaises(OSError):
                    supervisor.main()
                kill_group.assert_any_call(child.pid, signal.SIGTERM)
                child.wait.assert_called_once()
                selector.close.assert_called_once()

class CacheTests(unittest.TestCase):
    def test_cached_cli_reports_diagnostic_when_manifest_is_invalid(self):
        import importlib.util
        spec = importlib.util.spec_from_file_location('cache_under_test', SUPERVISOR)
        supervisor = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(supervisor)
        with tempfile.TemporaryDirectory() as directory:
            cache = Path(directory)
            manifest = cache / '_npx' / 'entry' / 'node_modules' / '@deepseek-ai' / 'dsh' / 'package.json'
            manifest.parent.mkdir(parents=True)
            manifest.write_text('{broken')

            result = supervisor.cached_cli('@deepseek-ai/dsh@0.1.5-rc.1', cache=cache, node='/missing/bin/node')

            self.assertIsNone(result.path)
            self.assertEqual(len(result.diagnostics), 1)
            self.assertIn('JSONDecodeError', result.diagnostics[0])
            self.assertNotIn(directory, result.diagnostics[0])


if __name__ == '__main__':
    unittest.main(verbosity=2)
