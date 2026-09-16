#!/usr/bin/env python3
"""One owned DSH process group. STOP or controller EOF terminates only this group."""
import argparse
import fcntl
import json
import os
from pathlib import Path
import selectors
import shutil
import signal
import socket
import subprocess
import sys
import tempfile
import time
from typing import NamedTuple, Optional, Tuple


class CachedCli(NamedTuple):
    path: Optional[Path]
    diagnostics: Tuple[str, ...]


def cached_cli(package, cache=None, node=None):
    """Return the exact installed CLI and diagnostics without logging or installing."""
    version = package.rsplit('@', 1)[1]
    cache = cache or Path(os.environ.get('npm_config_cache', str(Path.home() / '.npm')))
    diagnostics = []
    candidates = []
    node = node or shutil.which('node')
    if node:
        candidates.append(Path(node).resolve().parent.parent / 'lib/node_modules/@deepseek-ai/dsh/package.json')
    cached = []
    for manifest in cache.glob('_npx/*/node_modules/@deepseek-ai/dsh/package.json'):
        try:
            cached.append((manifest.stat().st_mtime, manifest))
        except FileNotFoundError:
            continue  # npm may evict a cache entry during discovery.
        except OSError as error:
            diagnostics.append('Cannot inspect harness cache (' + type(error).__name__ + ').')
    candidates.extend(path for _, path in sorted(cached, reverse=True))
    for manifest in candidates:
        try:
            metadata = json.loads(manifest.read_text())
            if metadata.get('name') != '@deepseek-ai/dsh' or metadata.get('version') != version:
                continue
            entry = metadata.get('bin', {}).get('dsh')
            if not isinstance(entry, str):
                raise ValueError('Invalid CLI entry')
            cli = (manifest.parent / entry).resolve()
            if cli.is_file() and manifest.parent.resolve() in cli.parents:
                return CachedCli(cli, tuple(diagnostics))
            diagnostics.append('Cached harness CLI is missing or outside its package.')
        except FileNotFoundError:
            continue  # An absent global installation is an ordinary cache miss.
        except (OSError, ValueError, AttributeError) as error:
            diagnostics.append('Cannot read cached harness package (' + type(error).__name__ + ').')
    return CachedCli(None, tuple(diagnostics))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--port', type=int, required=True)
    parser.add_argument('--package', default='@deepseek-ai/dsh@0.1.1-rc.2')
    args = parser.parse_args()
    if not 1 <= args.port <= 65535:
        parser.error('Invalid port')
    if not args.package.startswith('@deepseek-ai/dsh@'):
        parser.error('Expected a versioned @deepseek-ai/dsh package')
    lookup = cached_cli(args.package)
    for diagnostic in lookup.diagnostics:
        print("[supervisor] " + diagnostic, file=sys.stderr, flush=True)
    cli = lookup.path
    if cli and shutil.which('node'):
        command = ['node', str(cli)]
        print('[supervisor] Using installed DSH ' + args.package + ': ' + str(cli), flush=True)
    else:
        if not shutil.which('npx'):
            raise RuntimeError('npx not found. Check Node/nvm in your Ubuntu login shell.')
        command = ['npx', '--yes', '--loglevel', 'info', args.package]
        print('[supervisor] DSH is not cached; npm is resolving/installing it. This may take several minutes.', flush=True)

    # Lock is released by the OS, including on a crash; no stale PID files.
    state = Path(tempfile.gettempdir()) / ('llama-llm-servy-' + str(os.getuid()))
    state.mkdir(mode=0o700, exist_ok=True)
    if state.is_symlink() or state.stat().st_uid != os.getuid():
        raise RuntimeError('Invalid supervisor lock directory')
    with open(state / ('port-' + str(args.port) + '.lock'), 'a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise RuntimeError('Another launcher already owns DSH on this port')
        with socket.socket() as probe:
            # Ignore TIME_WAIT from a stopped server while still rejecting live listeners.
            probe.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            try:
                probe.bind(('127.0.0.1', args.port))
            except OSError:
                raise RuntimeError('DSH port is occupied; the existing process was not stopped')

        stopping = [False]
        def request_stop(signum, frame):
            stopping[0] = True
        signal.signal(signal.SIGTERM, request_stop)
        signal.signal(signal.SIGINT, request_stop)
        child = None
        selector = None
        code = 0
        try:
            child = subprocess.Popen(
                command + ['web', '--host', '127.0.0.1',
                 '--port', str(args.port), '--no-open'],
                stdin=subprocess.DEVNULL, start_new_session=True)
            print('[supervisor] DSH started; controller owns process group ' + str(child.pid), flush=True)
            selector = selectors.DefaultSelector()
            selector.register(sys.stdin, selectors.EVENT_READ)
            pending = b''
            code = 0
            while not stopping[0]:
                # waitid WNOWAIT observes termination without releasing/reusing the PID.
                info = os.waitid(os.P_PID, child.pid, os.WEXITED | os.WNOHANG | os.WNOWAIT)
                if info is not None:
                    code = info.si_status or 1
                    print('[supervisor] DSH exited unexpectedly: ' + str(code), flush=True)
                    break
                for key, mask in selector.select(timeout=0.25):
                    data = os.read(key.fd, 4096)
                    if not data:
                        stopping[0] = True
                        break
                    pending += data
                    while b'\n' in pending:
                        line, pending = pending.split(b'\n', 1)
                        if line.strip().lower() == b'stop':
                            stopping[0] = True
                        elif line.strip().lower() == b'status':
                            print('[supervisor] running', flush=True)
        finally:
            # Group leader stays unreaped until all signals have been sent, preventing PID reuse.
            try:
                if child is not None:
                    try:
                        os.killpg(child.pid, signal.SIGTERM)
                        time.sleep(2)
                        os.killpg(child.pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass  # The owned group has already exited.
                    child.wait()
            finally:
                if selector is not None:
                    selector.close()
            print('[supervisor] owned DSH process group stopped', flush=True)
    return code


if __name__ == '__main__':
    try:
        sys.exit(main())
    except Exception as exc:
        print('[supervisor] ERROR: ' + str(exc), file=sys.stderr, flush=True)
        sys.exit(1)
