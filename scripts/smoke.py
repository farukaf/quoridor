"""Integration smoke against a running application (default http://127.0.0.1:8080)."""
import json
import sys
import urllib.request
import uuid

base = (sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:8080').rstrip('/')
for path, expected in [('/', b'Welcome'), (f'/Room/{uuid.uuid4()}', b'blazor'),
                       ('/app.js', b'GetTimezone'), ('/app.css', b'{'),
                       ('/_framework/blazor.web.js', b'Blazor')]:
    with urllib.request.urlopen(base + path, timeout=20) as response:
        assert response.status == 200, path
        assert expected in response.read(), path
        print(f'PASS GET {path}')
request = urllib.request.Request(base + '/_blazor/negotiate?negotiateVersion=1', data=b'', method='POST')
with urllib.request.urlopen(request, timeout=20) as response:
    payload = json.load(response)
    assert payload['connectionToken']
    assert any(t['transport'] == 'WebSockets' for t in payload['availableTransports'])
    print('PASS SignalR negotiation with WebSockets transport')
