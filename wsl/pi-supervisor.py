#!/usr/bin/env python3
"""Own one isolated tmux server. Terminal clients may detach; STOP/EOF ends Pi."""
import argparse
import json
import os
from pathlib import Path
import selectors
import shlex
import shutil
import signal
import subprocess
import sys
import tempfile
import time


def emit(state, **fields):
    print(json.dumps(dict(state=state, **fields)), flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--executable', default='pi')
    parser.add_argument('--provider', required=True)
    parser.add_argument('--model', required=True)
    args = parser.parse_args()
    executable = shutil.which(args.executable)
    tmux = shutil.which('tmux')
    if not executable or not tmux:
        emit('error', code='PiMissingExecutable' if not executable else 'PiMissingTmux')
        return 1
    stopping = False
    def stop(signum, frame):
        nonlocal stopping
        stopping = True
    signal.signal(signal.SIGTERM, stop)
    signal.signal(signal.SIGINT, stop)
    # A private socket and config isolate both ownership and user tmux customizations.
    with tempfile.TemporaryDirectory(prefix='llm-servy-pi-', dir='/tmp') as directory:
        socket = str(Path(directory) / 'tmux.sock')
        config = Path(directory) / 'tmux.conf'
        # Pi requests extended keyboard reporting; CSI-u is available from tmux 3.5.
        # -q retains the default xterm format on tmux 3.2–3.4.
        config.write_text(
            'set -g remain-on-exit on\n'
            'set -g status off\n'
            'set -g default-terminal "tmux-256color"\n'
            'set -s extended-keys on\n'
            'set -sq extended-keys-format csi-u\n')
        def command(*values, check=True):
            return subprocess.run([tmux, '-S', socket, *values], capture_output=True, text=True,
                                  timeout=3, check=check)
        pane_pid = None
        try:
            command('-f', str(config), 'new-session', '-d', '-s', 'pi', '-x', '120', '-y', '35',
                    '-c', os.getcwd(), 'exec ' + shlex.join([executable, '--provider', args.provider, '--model', args.model]))
            pane_pid = int(command('display-message', '-p', '-t', 'pi', '#{pane_pid}').stdout.strip())
            emitted_running = False
            with selectors.DefaultSelector() as selector:
                selector.register(sys.stdin, selectors.EVENT_READ)
                pending = b''
                while not stopping:
                    result = command('display-message', '-p', '-t', 'pi', '#{pane_dead}:#{pane_dead_status}', check=False)
                    if result.returncode:
                        emit('error', code='PiSessionLost')
                        return 1
                    dead, _, status = result.stdout.strip().partition(':')
                    if dead == '1':
                        code = int(status) if status.isdigit() else 1
                        emit('exited', exitCode=code)
                        return code
                    if not emitted_running:
                        emit('running', socket=socket)
                        emitted_running = True
                    for key, _ in selector.select(timeout=.5):
                        data = os.read(key.fd, 4096)
                        if not data:
                            stopping = True
                            break
                        pending += data
                        while b'\n' in pending:
                            line, pending = pending.split(b'\n', 1)
                            if line.strip().lower() == b'stop':
                                stopping = True
            return 0
        finally:
            # Terminate the pane's process group before closing its pseudo terminal.
            pane = command('display-message', '-p', '-t', 'pi', '#{pane_dead}:#{pane_pid}', check=False)
            if pane_pid and pane.returncode == 0 and pane.stdout.strip() == '0:' + str(pane_pid):
                try:
                    os.killpg(pane_pid, signal.SIGTERM)
                    deadline = time.monotonic() + 2
                    while time.monotonic() < deadline:
                        result = command('display-message', '-p', '-t', 'pi', '#{pane_dead}', check=False)
                        if result.returncode or result.stdout.strip() == '1':
                            break
                        time.sleep(.05)
                    # Only signal a still-live pane, avoiding a stale/reused process-group ID.
                    result = command('display-message', '-p', '-t', 'pi', '#{pane_dead}:#{pane_pid}', check=False)
                    if result.returncode == 0 and result.stdout.strip() == '0:' + str(pane_pid):
                        os.killpg(pane_pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
            if Path(socket).exists():
                result = command('kill-server', check=False)
                if result.returncode and Path(socket).exists():
                    emit('error', code='PiSupervisorFailed')
                    raise RuntimeError('Unable to stop the private tmux server')
            emit('stopped')


if __name__ == '__main__':
    try:
        sys.exit(main())
    except Exception:
        emit('error', code='PiSupervisorFailed')
        sys.exit(1)
