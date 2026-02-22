//8812938
// ========== Auth ==========
let token = localStorage.getItem('admin_token') || '';

function getHeaders() {
    return { 'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json' };
}

async function apiFetch(url, options = {}) {
    options.headers = { ...getHeaders(), ...(options.headers || {}) };
    const res = await fetch(url, options);
    if (res.status === 401) { doLogout(); throw new Error('Unauthorized'); }
    return res.json();
}

async function doLogin() {
    const input = document.getElementById('login-token');
    const err = document.getElementById('login-error');
    try {
        const res = await fetch('/api/auth/validate', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ token: input.value })
        });
        const data = await res.json();
        if (data.valid) {
            token = input.value;
            localStorage.setItem('admin_token', token);
            showApp();
        } else {
            err.style.display = 'block';
        }
    } catch (e) {
        err.style.display = 'block';
    }
}

function doLogout() {
    token = '';
    localStorage.removeItem('admin_token');
    document.getElementById('app').style.display = 'none';
    document.getElementById('login-screen').style.display = 'flex';
}

function showApp() {
    document.getElementById('login-screen').style.display = 'none';
    document.getElementById('app').style.display = 'flex';
    loadStatus();
}

// Auto-login: try saved token or blank (auto-enters when no ADMIN_TOKEN is configured)
fetch('/api/auth/validate', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ token: token || '' })
}).then(r => r.json()).then(d => {
    if (d.valid) showApp();
    else doLogout();
}).catch(() => doLogout());

document.getElementById('login-token').addEventListener('keydown', e => {
    if (e.key === 'Enter') doLogin();
});

// ========== Navigation ==========
let currentPage = 'status';
let refreshTimers = {};

document.querySelectorAll('.nav-item').forEach(item => {
    item.addEventListener('click', () => {
        document.querySelectorAll('.nav-item').forEach(n => n.classList.remove('active'));
        item.classList.add('active');
        document.querySelectorAll('.page').forEach(p => p.classList.remove('active'));
        currentPage = item.dataset.page;
        document.getElementById('page-' + currentPage).classList.add('active');

        // Load page data
        clearAllTimers();
        switch (currentPage) {
            case 'status': loadStatus(); startTimer('status', loadStatus, 5000); break;
            case 'voice': loadVoice(); startTimer('voice', loadVoice, 3000); break;
            case 'players': loadPlayers(); break;
            case 'redis': break;
            case 'admin': break;
            case 'logs': loadLogs(); break;
        }
    });
});

function startTimer(name, fn, ms) {
    refreshTimers[name] = setInterval(fn, ms);
}

function clearAllTimers() {
    Object.values(refreshTimers).forEach(t => clearInterval(t));
    refreshTimers = {};
}

// Start default timer
startTimer('status', loadStatus, 5000);

// ========== Server Status ==========
async function loadStatus() {
    try {
        const data = await apiFetch('/api/status/overview');
        updateStatusCard('card-redis', data.redis);
        updateStatusCard('card-worldserver', data.worldserver);
        updateStatusCard('card-appserver', data.appserver);
        document.getElementById('status-refresh').textContent = 'Last update: ' + new Date().toLocaleTimeString();
    } catch (e) {
        console.error('Status load error:', e);
    }
}

function updateStatusCard(cardId, data) {
    const card = document.getElementById(cardId);
    if (!card) return;
    const dot = card.querySelector('.status-dot');
    const value = card.querySelector('.card-value');
    const detail = card.querySelector('.card-detail');

    const status = data?.status || 'unknown';
    dot.className = 'status-dot ' + (status === 'running' ? 'green' : status === 'error' ? 'red' : 'yellow');

    if (cardId === 'card-redis' && data?.stats) {
        value.textContent = data.stats.used_memory || '--';
        detail.textContent = `Clients: ${data.stats.connected_clients || '?'} | Up: ${formatUptime(data.stats.uptime_seconds)} | v${data.stats.redis_version || '?'}`;
    } else if (cardId === 'card-worldserver' && data?.info) {
        value.textContent = (data.info.players || '0') + ' players';
        detail.textContent = `Version: ${data.info.version || '?'} | Up: ${formatUptime(data.info.uptime)} | Max: ${data.info.maxPlayers || '?'}`;
    } else if (cardId === 'card-appserver') {
        value.textContent = status === 'running' ? 'Online' : 'Offline';
        detail.textContent = data?.statusCode ? `HTTP ${data.statusCode}` : (data?.error || status);
    } else {
        value.textContent = status;
        detail.textContent = data?.info || data?.error || '';
    }
}

function formatUptime(seconds) {
    if (!seconds) return '?';
    const s = parseInt(seconds);
    if (s < 60) return s + 's';
    if (s < 3600) return Math.floor(s / 60) + 'm';
    if (s < 86400) return Math.floor(s / 3600) + 'h ' + Math.floor((s % 3600) / 60) + 'm';
    return Math.floor(s / 86400) + 'd ' + Math.floor((s % 86400) / 3600) + 'h';
}

// ========== Voice Chat ==========
async function loadVoice() {
    try {
        const data = await apiFetch('/api/voice/stats');
        if (data.status === 'no_data') {
            document.getElementById('v-auth').textContent = '--';
            document.getElementById('v-speakers').textContent = '--';
            document.getElementById('v-pps').textContent = '--';
            document.getElementById('v-ratelimit').textContent = '--';
            document.getElementById('v-cells').textContent = '--';
            document.getElementById('v-gridplayers').textContent = '--';
            document.getElementById('v-caphits').textContent = '--';
            return;
        }
        document.getElementById('v-auth').textContent = data.authenticatedPlayers || '0';
        document.getElementById('v-speakers').textContent = data.activeSpeakers || '0';
        document.getElementById('v-pps').textContent = data.packetsPerSecond || '0';
        document.getElementById('v-ratelimit').textContent = data.rateLimitHits || '0';
        document.getElementById('v-cells').textContent = data.occupiedCells || '0';
        document.getElementById('v-gridplayers').textContent = data.trackedPlayers || '0';
        document.getElementById('v-caphits').textContent = data.speakerCapHits || '0';
        document.getElementById('voice-refresh').textContent = 'Last update: ' + new Date().toLocaleTimeString();
    } catch (e) {
        console.error('Voice load error:', e);
    }
}

// ========== Players ==========
async function loadPlayers() {
    try {
        const search = document.getElementById('player-search')?.value || '';
        const url = search ? `/api/players/online?search=${encodeURIComponent(search)}` : '/api/players/online';
        const data = await apiFetch(url);
        const tbody = document.getElementById('players-tbody');
        if (!data.players || data.players.length === 0) {
            tbody.innerHTML = '<tr><td colspan="5" style="text-align:center;color:#666;">No players online</td></tr>';
            return;
        }
        tbody.innerHTML = data.players.map(p => `
            <tr>
                <td>${esc(p.name || '?')}</td>
                <td>${p.accountId || '?'}</td>
                <td>${esc(p.worldName || p.worldId || '?')}</td>
                <td><span class="status-dot ${p.voiceConnected ? 'green' : 'red'}"></span>${p.voiceConnected ? 'Yes' : 'No'}</td>
                <td>${p.isSpeaking ? 'Speaking' : '-'}</td>
            </tr>
        `).join('');
    } catch (e) {
        console.error('Players load error:', e);
    }
}

document.getElementById('player-search')?.addEventListener('keydown', e => {
    if (e.key === 'Enter') loadPlayers();
});

// ========== Redis Browser ==========
let redisCursor = 0;

async function loadRedisKeys(append = false) {
    try {
        const pattern = document.getElementById('redis-pattern').value || '*';
        if (!append) redisCursor = 0;
        const data = await apiFetch(`/api/redis/keys?pattern=${encodeURIComponent(pattern)}&count=50&cursor=${redisCursor}`);
        const tbody = document.getElementById('redis-keys-tbody');

        if (!append) tbody.innerHTML = '';
        if (data.keys.length === 0 && !append) {
            tbody.innerHTML = '<tr><td colspan="3" style="text-align:center;color:#666;">No keys found</td></tr>';
        }

        data.keys.forEach(key => {
            const tr = document.createElement('tr');
            tr.style.cursor = 'pointer';
            tr.onclick = () => loadRedisKeyValue(key);
            tr.innerHTML = `<td>${esc(key)}</td><td>-</td><td>-</td>`;
            tbody.appendChild(tr);
        });

        redisCursor = data.nextCursor;
        document.getElementById('redis-load-more').style.display = data.nextCursor > 0 ? 'inline-block' : 'none';
    } catch (e) {
        console.error('Redis keys error:', e);
    }
}

function loadMoreRedisKeys() { loadRedisKeys(true); }

async function loadRedisKeyValue(key) {
    try {
        const data = await apiFetch(`/api/redis/key/${encodeURIComponent(key)}`);
        const viewer = document.getElementById('redis-value-viewer');
        let content = `Key: ${data.key}\nType: ${data.type}\nTTL: ${data.ttl === -1 ? 'No expiry' : data.ttl + 's'}\n\n`;

        if (data.type === 'hash' && typeof data.value === 'object') {
            content += 'Fields:\n';
            for (const [k, v] of Object.entries(data.value)) {
                content += `  ${k}: ${v}\n`;
            }
        } else if (Array.isArray(data.value)) {
            content += 'Values:\n';
            data.value.forEach((v, i) => { content += `  [${i}] ${typeof v === 'object' ? JSON.stringify(v) : v}\n`; });
        } else {
            content += 'Value:\n  ' + (data.value || '(empty)');
        }

        viewer.textContent = content;
    } catch (e) {
        document.getElementById('redis-value-viewer').textContent = 'Error loading key: ' + e.message;
    }
}

document.getElementById('redis-pattern')?.addEventListener('keydown', e => {
    if (e.key === 'Enter') loadRedisKeys();
});

// ========== Admin Commands ==========
async function kickPlayer() {
    const id = document.getElementById('kick-account-id').value;
    if (!id) return;
    showConfirm(`Kick account ${id}?`, async () => {
        try {
            const data = await apiFetch('/api/admin/kick', {
                method: 'POST', body: JSON.stringify({ accountId: parseInt(id) })
            });
            showFeedback('kick-feedback', data.message, 'success');
        } catch (e) {
            showFeedback('kick-feedback', e.message, 'error');
        }
    });
}

async function sendAnnouncement() {
    const msg = document.getElementById('announce-message').value;
    if (!msg) return;
    try {
        const data = await apiFetch('/api/admin/announce', {
            method: 'POST', body: JSON.stringify({ message: msg })
        });
        showFeedback('announce-feedback', data.message, 'success');
        document.getElementById('announce-message').value = '';
    } catch (e) {
        showFeedback('announce-feedback', e.message, 'error');
    }
}

async function restartVoice() {
    showConfirm('Restart voice system?', async () => {
        try {
            const data = await apiFetch('/api/admin/voice/restart', { method: 'POST' });
            showFeedback('voice-admin-feedback', data.message, 'success');
        } catch (e) {
            showFeedback('voice-admin-feedback', e.message, 'error');
        }
    });
}

async function toggleTestMode() {
    const toggle = document.getElementById('testmode-toggle');
    const enabled = !toggle.classList.contains('on');
    try {
        const data = await apiFetch('/api/admin/voice/testmode', {
            method: 'POST', body: JSON.stringify({ enabled })
        });
        toggle.classList.toggle('on', enabled);
        showFeedback('voice-admin-feedback', data.message, 'success');
    } catch (e) {
        showFeedback('voice-admin-feedback', e.message, 'error');
    }
}

// ========== Logs ==========
let currentLogSource = 'worldserver';

document.querySelectorAll('.tab[data-log]').forEach(tab => {
    tab.addEventListener('click', () => {
        document.querySelectorAll('.tab[data-log]').forEach(t => t.classList.remove('active'));
        tab.classList.add('active');
        currentLogSource = tab.dataset.log;
        loadLogs();
    });
});

async function loadLogs() {
    try {
        const lines = document.getElementById('log-lines').value;
        const filter = document.getElementById('log-filter').value;
        let url = `/api/logs/${currentLogSource}?lines=${lines}`;
        if (filter) url += `&filter=${encodeURIComponent(filter)}`;

        const data = await apiFetch(url);
        const output = document.getElementById('log-output');

        if (data.lines && data.lines.length > 0) {
            output.textContent = data.lines.join('\n');
            output.scrollTop = output.scrollHeight;
        } else {
            output.textContent = 'No logs available. WorldServer/AppServer may need log forwarding enabled.';
        }
    } catch (e) {
        document.getElementById('log-output').textContent = 'Error loading logs: ' + e.message;
    }
}

// ========== Helpers ==========
function esc(str) {
    const div = document.createElement('div');
    div.textContent = str;
    return div.innerHTML;
}

function showFeedback(id, message, type) {
    const el = document.getElementById(id);
    el.textContent = message;
    el.className = 'feedback ' + type;
    setTimeout(() => { el.className = 'feedback'; }, 5000);
}

function showConfirm(text, onConfirm) {
    const overlay = document.getElementById('confirm-overlay');
    document.getElementById('confirm-text').textContent = text;
    overlay.classList.add('active');
    document.getElementById('confirm-yes').onclick = () => {
        hideConfirm();
        onConfirm();
    };
}

function hideConfirm() {
    document.getElementById('confirm-overlay').classList.remove('active');
}
