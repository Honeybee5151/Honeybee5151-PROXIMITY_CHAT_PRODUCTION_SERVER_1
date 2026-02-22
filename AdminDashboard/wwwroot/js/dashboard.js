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
let redisCurrentKey = null;
let redisCurrentType = null;

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
        redisCurrentKey = data.key;
        redisCurrentType = data.type;

        // Update header
        document.getElementById('redis-value-header').style.display = 'flex';
        document.getElementById('redis-current-key').textContent = data.key;
        document.getElementById('redis-current-type').textContent = data.type;
        document.getElementById('redis-current-ttl').textContent = data.ttl === -1 ? 'No expiry' : data.ttl + 's TTL';

        // Highlight selected row in keys list
        document.querySelectorAll('#redis-keys-tbody tr').forEach(r => r.classList.remove('selected'));
        document.querySelectorAll('#redis-keys-tbody tr').forEach(r => {
            if (r.querySelector('td')?.textContent === data.key) r.classList.add('selected');
        });

        const content = document.getElementById('redis-value-content');

        if (data.type === 'hash' && typeof data.value === 'object') {
            renderHashEditor(content, data.key, data.value);
        } else if (data.type === 'list' && Array.isArray(data.value)) {
            renderListEditor(content, data.key, data.value);
        } else if (data.type === 'set' && Array.isArray(data.value)) {
            renderSetEditor(content, data.key, data.value);
        } else if (data.type === 'sortedset' && Array.isArray(data.value)) {
            renderSortedSetEditor(content, data.key, data.value);
        } else {
            renderStringEditor(content, data.key, data.value);
        }
    } catch (e) {
        document.getElementById('redis-value-content').innerHTML = `<div class="redis-empty-state">Error: ${esc(e.message)}</div>`;
    }
}

function renderStringEditor(container, key, value) {
    container.innerHTML = `
        <div class="redis-editor">
            <textarea class="redis-edit-textarea" id="redis-string-val">${esc(value || '')}</textarea>
            <button class="btn btn-success btn-sm" onclick="redisSaveString()">Save</button>
        </div>`;
}

function renderHashEditor(container, key, hash) {
    const entries = Object.entries(hash);
    let html = `<div class="redis-editor">
        <table class="redis-edit-table"><thead><tr><th>Field</th><th>Value</th><th></th></tr></thead><tbody>`;
    entries.forEach(([field, val]) => {
        html += `<tr>
            <td class="redis-field-name">${esc(field)}</td>
            <td><input class="redis-edit-input" data-field="${esc(field)}" value="${esc(val)}" /></td>
            <td class="redis-row-actions">
                <button class="btn btn-success btn-xs" onclick="redisSaveHashField(this)" title="Save">&#10003;</button>
                <button class="btn btn-danger btn-xs" onclick="redisDeleteHashField('${esc(field)}')" title="Delete">&#10005;</button>
            </td></tr>`;
    });
    html += `</tbody></table>
        <div class="redis-add-row">
            <input class="redis-edit-input" id="redis-new-hash-field" placeholder="New field name" />
            <input class="redis-edit-input" id="redis-new-hash-value" placeholder="Value" />
            <button class="btn btn-primary btn-sm" onclick="redisAddHashField()">Add Field</button>
        </div></div>`;
    container.innerHTML = html;
}

function renderListEditor(container, key, items) {
    let html = `<div class="redis-editor">
        <table class="redis-edit-table"><thead><tr><th>#</th><th>Value</th><th></th></tr></thead><tbody>`;
    items.forEach((val, i) => {
        html += `<tr>
            <td class="redis-field-name">${i}</td>
            <td><input class="redis-edit-input" data-index="${i}" value="${esc(val)}" /></td>
            <td class="redis-row-actions">
                <button class="btn btn-success btn-xs" onclick="redisSaveListItem(this)" title="Save">&#10003;</button>
                <button class="btn btn-danger btn-xs" onclick="redisDeleteListItem('${esc(val)}')" title="Delete">&#10005;</button>
            </td></tr>`;
    });
    html += `</tbody></table>
        <div class="redis-add-row">
            <input class="redis-edit-input" id="redis-new-list-value" placeholder="New value" style="flex:1" />
            <button class="btn btn-primary btn-sm" onclick="redisAddListItem()">Push</button>
        </div></div>`;
    container.innerHTML = html;
}

function renderSetEditor(container, key, members) {
    let html = `<div class="redis-editor">
        <table class="redis-edit-table"><thead><tr><th>Member</th><th></th></tr></thead><tbody>`;
    members.forEach(val => {
        html += `<tr>
            <td>${esc(val)}</td>
            <td class="redis-row-actions">
                <button class="btn btn-danger btn-xs" onclick="redisDeleteSetMember('${esc(val)}')" title="Remove">&#10005;</button>
            </td></tr>`;
    });
    html += `</tbody></table>
        <div class="redis-add-row">
            <input class="redis-edit-input" id="redis-new-set-value" placeholder="New member" style="flex:1" />
            <button class="btn btn-primary btn-sm" onclick="redisAddSetMember()">Add</button>
        </div></div>`;
    container.innerHTML = html;
}

function renderSortedSetEditor(container, key, entries) {
    let html = `<div class="redis-editor">
        <table class="redis-edit-table"><thead><tr><th>Member</th><th>Score</th><th></th></tr></thead><tbody>`;
    entries.forEach(e => {
        const member = e.member || e.Member || '';
        const score = e.score ?? e.Score ?? 0;
        html += `<tr>
            <td>${esc(member)}</td>
            <td><input class="redis-edit-input redis-score-input" data-member="${esc(member)}" value="${score}" type="number" step="any" /></td>
            <td class="redis-row-actions">
                <button class="btn btn-success btn-xs" onclick="redisSaveZSetScore(this)" title="Save">&#10003;</button>
                <button class="btn btn-danger btn-xs" onclick="redisDeleteZSetMember('${esc(member)}')" title="Remove">&#10005;</button>
            </td></tr>`;
    });
    html += `</tbody></table>
        <div class="redis-add-row">
            <input class="redis-edit-input" id="redis-new-zset-member" placeholder="Member" />
            <input class="redis-edit-input redis-score-input" id="redis-new-zset-score" placeholder="Score" type="number" step="any" value="0" />
            <button class="btn btn-primary btn-sm" onclick="redisAddZSetMember()">Add</button>
        </div></div>`;
    container.innerHTML = html;
}

// ---- Redis write operations ----

async function redisSaveString() {
    const val = document.getElementById('redis-string-val').value;
    try {
        await apiFetch(`/api/redis/key/${encodeURIComponent(redisCurrentKey)}`, {
            method: 'PUT', body: JSON.stringify({ value: val })
        });
        showFeedback('redis-feedback', 'Saved', 'success');
    } catch (e) { showFeedback('redis-feedback', e.message, 'error'); }
}

async function redisSaveHashField(btn) {
    const input = btn.closest('tr').querySelector('input');
    const field = input.dataset.field;
    try {
        await apiFetch(`/api/redis/hash/${encodeURIComponent(redisCurrentKey)}`, {
            method: 'PUT', body: JSON.stringify({ field, value: input.value })
        });
        showFeedback('redis-feedback', `Saved ${field}`, 'success');
    } catch (e) { showFeedback('redis-feedback', e.message, 'error'); }
}

async function redisDeleteHashField(field) {
    if (!confirm(`Delete field "${field}"?`)) return;
    try {
        await apiFetch(`/api/redis/hash/${encodeURIComponent(redisCurrentKey)}?field=${encodeURIComponent(field)}`, { method: 'DELETE' });
        showFeedback('redis-feedback', `Deleted ${field}`, 'success');
        loadRedisKeyValue(redisCurrentKey);
    } catch (e) { showFeedback('redis-feedback', e.message, 'error'); }
}

async function redisAddHashField() {
    const field = document.getElementById('redis-new-hash-field').value;
    const value = document.getElementById('redis-new-hash-value').value;
    if (!field) return;
    try {
        await apiFetch(`/api/redis/hash/${encodeURIComponent(redisCurrentKey)}`, {
            method: 'PUT', body: JSON.stringify({ field, value })
        });
        showFeedback('redis-feedback', `Added ${field}`, 'success');
        loadRedisKeyValue(redisCurrentKey);
    } catch (e) { showFeedback('redis-feedback', e.message, 'error'); }
}

async function redisSaveListItem(btn) {
    const input = btn.closest('tr').querySelector('input');
    const index = parseInt(input.dataset.index);
    try {
        await apiFetch(`/api/redis/list/${encodeURIComponent(redisCurrentKey)}`, {
            method: 'PUT', body: JSON.stringify({ index, value: input.value })
        });
        showFeedback('redis-feedback', `Saved [${index}]`, 'success');
    } catch (e) { showFeedback('redis-feedback', e.message, 'error'); }
}

async function redisDeleteListItem(value) {
    if (!confirm(`Remove item "${value.substring(0, 40)}"?`)) return;
    try {
        await apiFetch(`/api/redis/list/${encodeURIComponent(redisCurrentKey)}?value=${encodeURIComponent(value)}`, { method: 'DELETE' });
        showFeedback('redis-feedback', 'Removed', 'success');
        loadRedisKeyValue(redisCurrentKey);
    } catch (e) { showFeedback('redis-feedback', e.message, 'error'); }
}

async function redisAddListItem() {
    const value = document.getElementById('redis-new-list-value').value;
    try {
        await apiFetch(`/api/redis/list/${encodeURIComponent(redisCurrentKey)}`, {
            method: 'PUT', body: JSON.stringify({ value })
        });
        showFeedback('redis-feedback', 'Pushed', 'success');
        loadRedisKeyValue(redisCurrentKey);
    } catch (e) { showFeedback('redis-feedback', e.message, 'error'); }
}

async function redisDeleteSetMember(value) {
    if (!confirm(`Remove "${value.substring(0, 40)}"?`)) return;
    try {
        await apiFetch(`/api/redis/set/${encodeURIComponent(redisCurrentKey)}?value=${encodeURIComponent(value)}`, { method: 'DELETE' });
        showFeedback('redis-feedback', 'Removed', 'success');
        loadRedisKeyValue(redisCurrentKey);
    } catch (e) { showFeedback('redis-feedback', e.message, 'error'); }
}

async function redisAddSetMember() {
    const value = document.getElementById('redis-new-set-value').value;
    if (!value) return;
    try {
        await apiFetch(`/api/redis/set/${encodeURIComponent(redisCurrentKey)}`, {
            method: 'PUT', body: JSON.stringify({ value })
        });
        showFeedback('redis-feedback', 'Added', 'success');
        loadRedisKeyValue(redisCurrentKey);
    } catch (e) { showFeedback('redis-feedback', e.message, 'error'); }
}

async function redisSaveZSetScore(btn) {
    const input = btn.closest('tr').querySelector('input');
    const member = input.dataset.member;
    const score = parseFloat(input.value) || 0;
    try {
        await apiFetch(`/api/redis/zset/${encodeURIComponent(redisCurrentKey)}`, {
            method: 'PUT', body: JSON.stringify({ member, score })
        });
        showFeedback('redis-feedback', `Saved ${member}`, 'success');
    } catch (e) { showFeedback('redis-feedback', e.message, 'error'); }
}

async function redisDeleteZSetMember(member) {
    if (!confirm(`Remove "${member.substring(0, 40)}"?`)) return;
    try {
        await apiFetch(`/api/redis/zset/${encodeURIComponent(redisCurrentKey)}?member=${encodeURIComponent(member)}`, { method: 'DELETE' });
        showFeedback('redis-feedback', 'Removed', 'success');
        loadRedisKeyValue(redisCurrentKey);
    } catch (e) { showFeedback('redis-feedback', e.message, 'error'); }
}

async function redisAddZSetMember() {
    const member = document.getElementById('redis-new-zset-member').value;
    const score = parseFloat(document.getElementById('redis-new-zset-score').value) || 0;
    if (!member) return;
    try {
        await apiFetch(`/api/redis/zset/${encodeURIComponent(redisCurrentKey)}`, {
            method: 'PUT', body: JSON.stringify({ member, score })
        });
        showFeedback('redis-feedback', 'Added', 'success');
        loadRedisKeyValue(redisCurrentKey);
    } catch (e) { showFeedback('redis-feedback', e.message, 'error'); }
}

async function redisDeleteKey() {
    if (!confirm(`Delete entire key "${redisCurrentKey}"?`)) return;
    try {
        await apiFetch(`/api/redis/key/${encodeURIComponent(redisCurrentKey)}`, { method: 'DELETE' });
        showFeedback('redis-feedback', 'Key deleted', 'success');
        document.getElementById('redis-value-header').style.display = 'none';
        document.getElementById('redis-value-content').innerHTML = '<div class="redis-empty-state">Key deleted</div>';
        redisCurrentKey = null;
        loadRedisKeys();
    } catch (e) { showFeedback('redis-feedback', e.message, 'error'); }
}

function redisRefreshKey() {
    if (redisCurrentKey) loadRedisKeyValue(redisCurrentKey);
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
