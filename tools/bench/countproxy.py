#!/usr/bin/env python3
"""Counting + throttling forward proxy (HTTP and HTTPS CONNECT tunnels).
usage: countproxy.py <port> <bytes_per_sec_limit|0> <stats_file>"""
import asyncio, sys, time, json

PORT = int(sys.argv[1]); LIMIT = float(sys.argv[2]); STATS = sys.argv[3]
total = {"down": 0, "up": 0, "conns": 0}
HOSTLOG = STATS + ".hosts"
hosts = {}
hostbytes = {}
next_t = 0.0

async def throttle(n):
    global next_t
    if LIMIT <= 0: return
    now = time.monotonic()
    start = max(now, next_t)
    next_t = start + n / LIMIT
    if start - now > 0: await asyncio.sleep(start - now)

async def pipe(r, w, key, host=None):
    try:
        while True:
            data = await r.read(16384)
            if not data: break
            total[key] += len(data)
            if host: hostbytes[host] = hostbytes.get(host, 0) + len(data)
            if key == "down": await throttle(len(data))
            w.write(data); await w.drain()
    except Exception:
        pass
    finally:
        try: w.close()
        except Exception: pass

async def handle(cr, cw):
    try:
        head = await cr.readuntil(b"\r\n\r\n")
    except Exception:
        cw.close(); return
    total["up"] += len(head)
    first = head.split(b"\r\n", 1)[0].decode("latin1").split(" ")
    try:
        h = first[1].rsplit(":", 1)[0] if first[0] == "CONNECT" else first[1].split("/")[2]
        hosts[h] = hosts.get(h, 0) + 1
        pass
    except Exception: pass
    try:
        if first[0] == "CONNECT":
            host, port = first[1].rsplit(":", 1)
            ur, uw = await asyncio.open_connection(host, int(port))
            cw.write(b"HTTP/1.1 200 Connection established\r\n\r\n"); await cw.drain()
        else:
            from urllib.parse import urlsplit
            u = urlsplit(first[1]); host = u.hostname; port = u.port or 80
            ur, uw = await asyncio.open_connection(host, port)
            uw.write(head); await uw.drain()
    except Exception:
        cw.close(); return
    total["conns"] += 1
    await asyncio.gather(pipe(cr, uw, "up", h), pipe(ur, cw, "down", h))

async def dump():
    while True:
        open(HOSTLOG, 'w').write('\n'.join(f'{hostbytes.get(k,0)//1024:>7} KB  {n}x  {k}' for k, n in sorted(hosts.items(), key=lambda kv: -hostbytes.get(kv[0],0))))
        with open(STATS, "w") as f: json.dump(total, f)
        await asyncio.sleep(0.2)

async def main():
    srv = await asyncio.start_server(handle, "127.0.0.1", PORT)
    asyncio.create_task(dump())
    async with srv: await srv.serve_forever()

asyncio.run(main())
