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
            case 'redis': onRedisTabOpen(); break;
            case 'dungeons': loadDungeons(); break;
            case 'admin': loadMaintenanceState(); break;
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
let redisActiveTab = 'players';
let allKeysLoaded = false;

// Tab switching within Redis page
document.querySelectorAll('#redis-tabs .tab').forEach(tab => {
    tab.addEventListener('click', () => {
        document.querySelectorAll('#redis-tabs .tab').forEach(t => t.classList.remove('active'));
        tab.classList.add('active');
        redisActiveTab = tab.dataset.rtab;
        document.querySelectorAll('.redis-tab-content').forEach(c => c.classList.remove('active'));
        document.getElementById('rtab-' + redisActiveTab).classList.add('active');
        // Auto-load All Keys tab on first switch
        if (redisActiveTab === 'allkeys' && !allKeysLoaded) {
            allKeysLoaded = true;
            loadRedisKeys();
        }
    });
});

function onRedisTabOpen() {
    // Focus the player search input on open
    setTimeout(() => document.getElementById('player-redis-search')?.focus(), 50);
}

// ---- Player search ----
async function searchPlayer() {
    const search = document.getElementById('player-redis-search').value.trim();
    if (!search) return;
    const result = document.getElementById('player-lookup-result');
    const tbody = document.getElementById('player-keys-tbody');
    // Reset editor
    getHeaderEl().style.display = 'none';
    getContentEl().innerHTML = '<div class="redis-empty-state">Select a key to edit</div>';
    redisCurrentKey = null;

    try {
        const data = await apiFetch(`/api/redis/player?search=${encodeURIComponent(search)}`);
        if (!data.found) {
            result.innerHTML = `<div class="player-not-found">Player "${esc(search)}" not found</div>`;
            tbody.innerHTML = '<tr><td style="text-align:center;color:#555;">No results</td></tr>';
            return;
        }
        // Show player banner
        const acc = data.accountData || {};
        result.innerHTML = `<div class="player-banner">
            <div>
                <div class="player-name">${esc(data.name || '?')}</div>
                <div class="player-id">Account ID: ${esc(data.accountId)}</div>
            </div>
            <div class="player-stats">
                <span>Fame: ${esc(acc.fame || '0')}</span>
                <span>Credits: ${esc(acc.credits || '0')}</span>
                <span>Rank: ${esc(acc.rank || '0')}</span>
                <span>Guild ID: ${esc(acc.guildId || '0')}</span>
            </div>
        </div>`;
        // Show related keys
        tbody.innerHTML = '';
        (data.relatedKeys || []).forEach(key => {
            const tr = document.createElement('tr');
            tr.style.cursor = 'pointer';
            tr.onclick = () => loadRedisKeyValue(key);
            // Friendly label
            let label = key;
            if (key === `account.${data.accountId}`) label = 'Account Data';
            else if (key === `vault.${data.accountId}`) label = 'Vault';
            else if (key === `classStats.${data.accountId}`) label = 'Class Stats';
            else if (key === `alive.${data.accountId}`) label = 'Alive Characters';
            else if (key === `dead.${data.accountId}`) label = 'Dead Characters';
            else if (key.startsWith('char.')) label = 'Character ' + key.split('.').pop();
            tr.innerHTML = `<td><div style="font-size:13px;color:#e0e0e0;">${esc(label)}</div><div style="font-size:11px;color:#666;">${esc(key)}</div></td>`;
            tbody.appendChild(tr);
        });
        // Auto-open account data
        if (data.relatedKeys?.length > 0) {
            loadRedisKeyValue(data.relatedKeys[0]);
        }
    } catch (e) {
        result.innerHTML = `<div class="player-not-found">Error: ${esc(e.message)}</div>`;
    }
}

document.getElementById('player-redis-search')?.addEventListener('keydown', e => {
    if (e.key === 'Enter') searchPlayer();
});

// ---- Helpers to get current tab's elements ----
function getHeaderEl() {
    return redisActiveTab === 'players'
        ? document.getElementById('redis-value-header')
        : document.getElementById('redis-value-header-all');
}
function getContentEl() {
    return redisActiveTab === 'players'
        ? document.getElementById('redis-value-content')
        : document.getElementById('redis-value-content-all');
}
function getFeedbackId() {
    return redisActiveTab === 'players' ? 'redis-feedback' : 'redis-feedback-all';
}
function getKeysTbodyId() {
    return redisActiveTab === 'players' ? 'player-keys-tbody' : 'redis-keys-tbody';
}

// ---- All Keys tab ----
const redisGroupState = {}; // tracks collapsed/expanded state per group

function getKeyGroup(key) {
    const groupPrefixes = [
        { prefix: 'account.', label: 'Accounts' },
        { prefix: 'vault.', label: 'Vaults' },
        { prefix: 'char.', label: 'Characters' },
        { prefix: 'classStats.', label: 'Class Stats' },
        { prefix: 'alive.', label: 'Alive' },
        { prefix: 'dead.', label: 'Dead' },
        { prefix: 'market.', label: 'Market' },
        { prefix: 'legends.', label: 'Legends' },
    ];
    for (const g of groupPrefixes) {
        if (key.startsWith(g.prefix)) return g.label;
    }
    return 'Other';
}

async function loadRedisKeys(append = false) {
    try {
        const pattern = document.getElementById('redis-pattern').value || '*';
        if (!append) redisCursor = 0;
        const data = await apiFetch(`/api/redis/keys?pattern=${encodeURIComponent(pattern)}&count=50&cursor=${redisCursor}`);
        const tbody = document.getElementById('redis-keys-tbody');

        if (!append) tbody.innerHTML = '';
        if (data.keys.length === 0 && !append) {
            tbody.innerHTML = '<tr><td colspan="3" style="text-align:center;color:#666;">No keys found</td></tr>';
            return;
        }

        // Group keys by prefix
        const groups = {};
        data.keys.forEach(key => {
            const group = getKeyGroup(key);
            if (!groups[group]) groups[group] = [];
            groups[group].push(key);
        });

        // Sort groups: Other first, then alphabetical
        const groupOrder = Object.keys(groups).sort((a, b) => {
            if (a === 'Other') return -1;
            if (b === 'Other') return 1;
            return a.localeCompare(b);
        });

        groupOrder.forEach(group => {
            const keys = groups[group].sort((a, b) => a.localeCompare(b));
            const isCollapsed = redisGroupState[group] === false;
            const groupId = 'redis-group-' + group.replace(/\s+/g, '-');

            // Group header row
            const headerTr = document.createElement('tr');
            headerTr.className = 'redis-group-header';
            headerTr.style.cursor = 'pointer';
            headerTr.style.background = '#1e293b';
            headerTr.style.userSelect = 'none';
            headerTr.onclick = () => {
                redisGroupState[group] = !redisGroupState[group] !== false ? false : true;
                const rows = tbody.querySelectorAll(`.${groupId}`);
                const arrow = headerTr.querySelector('.group-arrow');
                rows.forEach(r => r.style.display = redisGroupState[group] === false ? 'none' : '');
                arrow.textContent = redisGroupState[group] === false ? '▶' : '▼';
            };
            headerTr.innerHTML = `<td colspan="3" style="font-weight:600;color:#94a3b8;padding:6px 10px;font-size:13px;"><span class="group-arrow" style="display:inline-block;width:16px;">${isCollapsed ? '▶' : '▼'}</span> ${esc(group)} <span style="color:#475569;font-weight:400;">(${keys.length})</span></td>`;
            tbody.appendChild(headerTr);

            // Key rows
            keys.forEach(key => {
                const tr = document.createElement('tr');
                tr.className = groupId;
                tr.style.cursor = 'pointer';
                if (isCollapsed) tr.style.display = 'none';
                tr.onclick = () => { redisActiveTab = 'allkeys'; loadRedisKeyValue(key); };
                tr.innerHTML = `<td style="padding-left:26px;">${esc(key)}</td><td>-</td><td>-</td>`;
                tbody.appendChild(tr);
            });
        });

        redisCursor = data.nextCursor;
        document.getElementById('redis-load-more').style.display = data.nextCursor > 0 ? 'inline-block' : 'none';
    } catch (e) {
        console.error('Redis keys error:', e);
    }
}

function loadMoreRedisKeys() { loadRedisKeys(true); }

// ---- Shared key value loader ----
async function loadRedisKeyValue(key) {
    try {
        const data = await apiFetch(`/api/redis/key/${encodeURIComponent(key)}`);
        redisCurrentKey = data.key;
        redisCurrentType = data.type;

        const header = getHeaderEl();
        const content = getContentEl();
        const keysId = getKeysTbodyId();

        // Update header
        header.style.display = 'flex';
        const prefix = redisActiveTab === 'players' ? '' : '-all';
        document.getElementById('redis-current-key' + prefix).textContent = data.key;
        document.getElementById('redis-current-type' + prefix).textContent = data.type;
        document.getElementById('redis-current-ttl' + prefix).textContent = data.ttl === -1 ? 'No expiry' : data.ttl + 's TTL';

        // Highlight selected row
        document.querySelectorAll(`#${keysId} tr`).forEach(r => r.classList.remove('selected'));
        document.querySelectorAll(`#${keysId} tr`).forEach(r => {
            const td = r.querySelector('td');
            if (td) {
                const text = td.textContent;
                if (text === data.key || td.querySelector('div:last-child')?.textContent === data.key) {
                    r.classList.add('selected');
                }
            }
        });

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
        getContentEl().innerHTML = `<div class="redis-empty-state">Error: ${esc(e.message)}</div>`;
    }
}

// ---- Renderers ----
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
        const safeField = field.replace(/'/g, "\\'").replace(/"/g, '&quot;');
        html += `<tr>
            <td class="redis-field-name">${esc(field)}</td>
            <td><input class="redis-edit-input" data-field="${esc(field)}" value="${esc(val)}" /></td>
            <td class="redis-row-actions">
                <button class="btn btn-success btn-xs" onclick="redisSaveHashField(this)" title="Save">&#10003;</button>
                <button class="btn btn-danger btn-xs" onclick="redisDeleteHashField('${safeField}')" title="Delete">&#10005;</button>
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
        const safeVal = val.replace(/'/g, "\\'").replace(/"/g, '&quot;');
        html += `<tr>
            <td class="redis-field-name">${i}</td>
            <td><input class="redis-edit-input" data-index="${i}" value="${esc(val)}" /></td>
            <td class="redis-row-actions">
                <button class="btn btn-success btn-xs" onclick="redisSaveListItem(this)" title="Save">&#10003;</button>
                <button class="btn btn-danger btn-xs" onclick="redisDeleteListItem('${safeVal}')" title="Delete">&#10005;</button>
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
        const safeVal = val.replace(/'/g, "\\'").replace(/"/g, '&quot;');
        html += `<tr>
            <td>${esc(val)}</td>
            <td class="redis-row-actions">
                <button class="btn btn-danger btn-xs" onclick="redisDeleteSetMember('${safeVal}')" title="Remove">&#10005;</button>
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
        const safeMember = member.replace(/'/g, "\\'").replace(/"/g, '&quot;');
        html += `<tr>
            <td>${esc(member)}</td>
            <td><input class="redis-edit-input redis-score-input" data-member="${esc(member)}" value="${score}" type="number" step="any" /></td>
            <td class="redis-row-actions">
                <button class="btn btn-success btn-xs" onclick="redisSaveZSetScore(this)" title="Save">&#10003;</button>
                <button class="btn btn-danger btn-xs" onclick="redisDeleteZSetMember('${safeMember}')" title="Remove">&#10005;</button>
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
function rFeedback(msg, type) { showFeedback(getFeedbackId(), msg, type); }

async function redisSaveString() {
    const val = document.getElementById('redis-string-val').value;
    try {
        await apiFetch(`/api/redis/key/${encodeURIComponent(redisCurrentKey)}`, {
            method: 'PUT', body: JSON.stringify({ value: val })
        });
        rFeedback('Saved', 'success');
    } catch (e) { rFeedback(e.message, 'error'); }
}

async function redisSaveHashField(btn) {
    const input = btn.closest('tr').querySelector('input');
    const field = input.dataset.field;
    try {
        await apiFetch(`/api/redis/hash/${encodeURIComponent(redisCurrentKey)}`, {
            method: 'PUT', body: JSON.stringify({ field, value: input.value })
        });
        rFeedback(`Saved ${field}`, 'success');
    } catch (e) { rFeedback(e.message, 'error'); }
}

async function redisDeleteHashField(field) {
    if (!confirm(`Delete field "${field}"?`)) return;
    try {
        await apiFetch(`/api/redis/hash/${encodeURIComponent(redisCurrentKey)}?field=${encodeURIComponent(field)}`, { method: 'DELETE' });
        rFeedback(`Deleted ${field}`, 'success');
        loadRedisKeyValue(redisCurrentKey);
    } catch (e) { rFeedback(e.message, 'error'); }
}

async function redisAddHashField() {
    const field = document.getElementById('redis-new-hash-field').value;
    const value = document.getElementById('redis-new-hash-value').value;
    if (!field) return;
    try {
        await apiFetch(`/api/redis/hash/${encodeURIComponent(redisCurrentKey)}`, {
            method: 'PUT', body: JSON.stringify({ field, value })
        });
        rFeedback(`Added ${field}`, 'success');
        loadRedisKeyValue(redisCurrentKey);
    } catch (e) { rFeedback(e.message, 'error'); }
}

async function redisSaveListItem(btn) {
    const input = btn.closest('tr').querySelector('input');
    const index = parseInt(input.dataset.index);
    try {
        await apiFetch(`/api/redis/list/${encodeURIComponent(redisCurrentKey)}`, {
            method: 'PUT', body: JSON.stringify({ index, value: input.value })
        });
        rFeedback(`Saved [${index}]`, 'success');
    } catch (e) { rFeedback(e.message, 'error'); }
}

async function redisDeleteListItem(value) {
    if (!confirm(`Remove item "${value.substring(0, 40)}"?`)) return;
    try {
        await apiFetch(`/api/redis/list/${encodeURIComponent(redisCurrentKey)}?value=${encodeURIComponent(value)}`, { method: 'DELETE' });
        rFeedback('Removed', 'success');
        loadRedisKeyValue(redisCurrentKey);
    } catch (e) { rFeedback(e.message, 'error'); }
}

async function redisAddListItem() {
    const value = document.getElementById('redis-new-list-value').value;
    try {
        await apiFetch(`/api/redis/list/${encodeURIComponent(redisCurrentKey)}`, {
            method: 'PUT', body: JSON.stringify({ value })
        });
        rFeedback('Pushed', 'success');
        loadRedisKeyValue(redisCurrentKey);
    } catch (e) { rFeedback(e.message, 'error'); }
}

async function redisDeleteSetMember(value) {
    if (!confirm(`Remove "${value.substring(0, 40)}"?`)) return;
    try {
        await apiFetch(`/api/redis/set/${encodeURIComponent(redisCurrentKey)}?value=${encodeURIComponent(value)}`, { method: 'DELETE' });
        rFeedback('Removed', 'success');
        loadRedisKeyValue(redisCurrentKey);
    } catch (e) { rFeedback(e.message, 'error'); }
}

async function redisAddSetMember() {
    const value = document.getElementById('redis-new-set-value').value;
    if (!value) return;
    try {
        await apiFetch(`/api/redis/set/${encodeURIComponent(redisCurrentKey)}`, {
            method: 'PUT', body: JSON.stringify({ value })
        });
        rFeedback('Added', 'success');
        loadRedisKeyValue(redisCurrentKey);
    } catch (e) { rFeedback(e.message, 'error'); }
}

async function redisSaveZSetScore(btn) {
    const input = btn.closest('tr').querySelector('input');
    const member = input.dataset.member;
    const score = parseFloat(input.value) || 0;
    try {
        await apiFetch(`/api/redis/zset/${encodeURIComponent(redisCurrentKey)}`, {
            method: 'PUT', body: JSON.stringify({ member, score })
        });
        rFeedback(`Saved ${member}`, 'success');
    } catch (e) { rFeedback(e.message, 'error'); }
}

async function redisDeleteZSetMember(member) {
    if (!confirm(`Remove "${member.substring(0, 40)}"?`)) return;
    try {
        await apiFetch(`/api/redis/zset/${encodeURIComponent(redisCurrentKey)}?member=${encodeURIComponent(member)}`, { method: 'DELETE' });
        rFeedback('Removed', 'success');
        loadRedisKeyValue(redisCurrentKey);
    } catch (e) { rFeedback(e.message, 'error'); }
}

async function redisAddZSetMember() {
    const member = document.getElementById('redis-new-zset-member').value;
    const score = parseFloat(document.getElementById('redis-new-zset-score').value) || 0;
    if (!member) return;
    try {
        await apiFetch(`/api/redis/zset/${encodeURIComponent(redisCurrentKey)}`, {
            method: 'PUT', body: JSON.stringify({ member, score })
        });
        rFeedback('Added', 'success');
        loadRedisKeyValue(redisCurrentKey);
    } catch (e) { rFeedback(e.message, 'error'); }
}

async function redisDeleteKey() {
    if (!confirm(`Delete entire key "${redisCurrentKey}"?`)) return;
    try {
        await apiFetch(`/api/redis/key/${encodeURIComponent(redisCurrentKey)}`, { method: 'DELETE' });
        rFeedback('Key deleted', 'success');
        getHeaderEl().style.display = 'none';
        getContentEl().innerHTML = '<div class="redis-empty-state">Key deleted</div>';
        redisCurrentKey = null;
        if (redisActiveTab === 'allkeys') loadRedisKeys();
    } catch (e) { rFeedback(e.message, 'error'); }
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

//8812938 — maintenance mode
async function toggleMaintenance() {
    const toggle = document.getElementById('maintenance-toggle');
    const enabled = !toggle.classList.contains('on');
    showConfirm(enabled ? 'Enable maintenance mode? All non-admin players will be kicked.' : 'Disable maintenance mode?', async () => {
        try {
            const data = await apiFetch('/api/admin/maintenance', {
                method: 'POST', body: JSON.stringify({ enabled })
            });
            toggle.classList.toggle('on', enabled);
            showFeedback('maintenance-feedback', data.message, 'success');
        } catch (e) {
            showFeedback('maintenance-feedback', e.message, 'error');
        }
    });
}

async function loadMaintenanceState() {
    try {
        const data = await apiFetch('/api/admin/maintenance');
        document.getElementById('maintenance-toggle').classList.toggle('on', data.enabled);
    } catch (e) { /* ignore */ }
}

//8812938 — server-wide loot event
async function setLootEvent() {
    const pct = parseInt(document.getElementById('loot-event-percent').value) || 0;
    if (pct <= 0) { showFeedback('lootevent-feedback', 'Enter a percentage > 0', 'error'); return; }
    showConfirm(`Set server-wide loot event to +${pct}%?`, async () => {
        try {
            const data = await apiFetch('/api/admin/lootevent', {
                method: 'POST', body: JSON.stringify({ percent: pct / 100 })
            });
            showFeedback('lootevent-feedback', data.message, 'success');
        } catch (e) {
            showFeedback('lootevent-feedback', e.message, 'error');
        }
    });
}

async function clearLootEvent() {
    try {
        const data = await apiFetch('/api/admin/lootevent', {
            method: 'POST', body: JSON.stringify({ percent: 0 })
        });
        showFeedback('lootevent-feedback', data.message, 'success');
        document.getElementById('loot-event-percent').value = '';
    } catch (e) {
        showFeedback('lootevent-feedback', e.message, 'error');
    }
}

//8812938 — server-wide XP/fame event
async function setExpEvent() {
    const pct = parseInt(document.getElementById('exp-event-percent').value) || 0;
    if (pct <= 0) { showFeedback('expevent-feedback', 'Enter a percentage > 0', 'error'); return; }
    showConfirm(`Set server-wide XP event to +${pct}%?`, async () => {
        try {
            const data = await apiFetch('/api/admin/expevent', {
                method: 'POST', body: JSON.stringify({ percent: pct / 100 })
            });
            showFeedback('expevent-feedback', data.message, 'success');
        } catch (e) {
            showFeedback('expevent-feedback', e.message, 'error');
        }
    });
}

async function clearExpEvent() {
    try {
        const data = await apiFetch('/api/admin/expevent', {
            method: 'POST', body: JSON.stringify({ percent: 0 })
        });
        showFeedback('expevent-feedback', data.message, 'success');
        document.getElementById('exp-event-percent').value = '';
    } catch (e) {
        showFeedback('expevent-feedback', e.message, 'error');
    }
}

//8812938 — gift all online players (items)
async function giftAll() {
    const itemName = document.getElementById('giftall-item').value.trim();
    if (!itemName) { showFeedback('giftall-feedback', 'Enter an item name', 'error'); return; }
    showConfirm(`Gift "${itemName}" to ALL online players?`, async () => {
        try {
            const data = await apiFetch('/api/admin/giftall', {
                method: 'POST', body: JSON.stringify({ itemName })
            });
            showFeedback('giftall-feedback', data.message, 'success');
        } catch (e) {
            showFeedback('giftall-feedback', e.message, 'error');
        }
    });
}

//8812938 — ban/unban
async function banPlayer() {
    const id = document.getElementById('ban-account-id').value;
    const reason = document.getElementById('ban-reason').value;
    if (!id) return;
    showConfirm(`Ban account ${id}?`, async () => {
        try {
            const data = await apiFetch('/api/admin/ban', {
                method: 'POST', body: JSON.stringify({ accountId: parseInt(id), reason })
            });
            showFeedback('ban-feedback', data.message, 'success');
        } catch (e) {
            showFeedback('ban-feedback', e.message, 'error');
        }
    });
}

async function unbanPlayer() {
    const id = document.getElementById('ban-account-id').value;
    if (!id) return;
    try {
        const data = await apiFetch('/api/admin/unban', {
            method: 'POST', body: JSON.stringify({ accountId: parseInt(id) })
        });
        showFeedback('ban-feedback', data.message, 'success');
    } catch (e) {
        showFeedback('ban-feedback', e.message, 'error');
    }
}

//8812938 — IP ban/unban
async function banIp() {
    const ip = document.getElementById('ipban-ip').value;
    const reason = document.getElementById('ipban-reason').value;
    if (!ip) return;
    showConfirm(`Ban IP ${ip}?`, async () => {
        try {
            const data = await apiFetch('/api/admin/banip', {
                method: 'POST', body: JSON.stringify({ ip, reason })
            });
            showFeedback('ipban-feedback', data.message, 'success');
        } catch (e) {
            showFeedback('ipban-feedback', e.message, 'error');
        }
    });
}

async function unbanIp() {
    const ip = document.getElementById('ipban-ip').value;
    if (!ip) return;
    try {
        const data = await apiFetch('/api/admin/unbanip', {
            method: 'POST', body: JSON.stringify({ ip })
        });
        showFeedback('ipban-feedback', data.message, 'success');
    } catch (e) {
        showFeedback('ipban-feedback', e.message, 'error');
    }
}

//8812938 — mute/unmute
async function mutePlayer() {
    const id = document.getElementById('mute-account-id').value;
    const minutes = parseInt(document.getElementById('mute-minutes').value) || 0;
    if (!id) return;
    try {
        const data = await apiFetch('/api/admin/mute', {
            method: 'POST', body: JSON.stringify({ accountId: parseInt(id), minutes })
        });
        showFeedback('mute-feedback', data.message, data.success ? 'success' : 'error');
    } catch (e) {
        showFeedback('mute-feedback', e.message, 'error');
    }
}

async function unmutePlayer() {
    const id = document.getElementById('mute-account-id').value;
    if (!id) return;
    try {
        const data = await apiFetch('/api/admin/unmute', {
            method: 'POST', body: JSON.stringify({ accountId: parseInt(id) })
        });
        showFeedback('mute-feedback', data.message, data.success ? 'success' : 'error');
    } catch (e) {
        showFeedback('mute-feedback', e.message, 'error');
    }
}

//8812938 — admin command line
async function sendAdminCommand() {
    const command = document.getElementById('cmd-command').value.trim();
    const parameter = document.getElementById('cmd-parameter').value.trim();
    if (!command) { showFeedback('cmd-feedback', 'Enter a command', 'error'); return; }
    try {
        const data = await apiFetch('/api/admin/command', {
            method: 'POST', body: JSON.stringify({ command, parameter })
        });
        showFeedback('cmd-feedback', data.message, 'success');
    } catch (e) {
        showFeedback('cmd-feedback', e.message, 'error');
    }
}

document.getElementById('cmd-command')?.addEventListener('keydown', e => {
    if (e.key === 'Enter') sendAdminCommand();
});
document.getElementById('cmd-parameter')?.addEventListener('keydown', e => {
    if (e.key === 'Enter') sendAdminCommand();
});

//8812938 — redis console
async function executeRedisCommand() {
    const cmd = document.getElementById('redis-cmd').value.trim();
    if (!cmd) return;
    const output = document.getElementById('redis-console-output');
    // Append command
    output.textContent += `\n> ${cmd}\n`;
    try {
        const data = await apiFetch('/api/admin/redis/execute', {
            method: 'POST', body: JSON.stringify({ command: cmd })
        });
        output.textContent += (data.result || '(nil)') + '\n';
    } catch (e) {
        output.textContent += `(error) ${e.message}\n`;
    }
    output.scrollTop = output.scrollHeight;
    document.getElementById('redis-cmd').value = '';
}

function clearRedisConsole() {
    document.getElementById('redis-console-output').textContent = 'Ready.';
}

document.getElementById('redis-cmd')?.addEventListener('keydown', e => {
    if (e.key === 'Enter') executeRedisCommand();
});

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

// ========== Dungeons ==========
async function loadDungeons() {
    const container = document.getElementById('dungeons-list');
    const feedback = document.getElementById('dungeons-feedback');
    feedback.style.display = 'none';
    container.innerHTML = '<div style="color:#555;text-align:center;padding:20px;">Loading...</div>';

    try {
        const data = await apiFetch('/api/dungeons/pending');
        if (!data.dungeons || data.dungeons.length === 0) {
            container.innerHTML = '<div style="color:#555;text-align:center;padding:40px;">No pending dungeons</div>';
            return;
        }

        let html = '<div class="table-container"><table><thead><tr>' +
            '<th>Title</th><th>Map</th><th>Custom Tiles</th><th>Mobs</th><th>Items</th><th>Created</th><th>Actions</th>' +
            '</tr></thead><tbody>';

        for (const d of data.dungeons) {
            const date = d.created_at ? new Date(d.created_at).toLocaleDateString() : '—';
            html += `<tr>
                <td><strong>${esc(d.title || 'Untitled')}</strong>${d.description ? '<br><small style="color:#888;">' + esc(d.description) + '</small>' : ''}</td>
                <td>${d.has_map ? '<span style="color:#22c55e;">JM</span>' : d.has_xml ? '<span style="color:#8b5cf6;">XML</span>' : '—'}</td>
                <td>${d.has_custom_tiles ? '<span style="color:#ef4444;">Yes</span>' : 'No'}</td>
                <td>${d.mob_count}</td>
                <td>${d.item_count}</td>
                <td>${date}</td>
                <td style="white-space:nowrap;">
                    <button class="btn btn-primary btn-sm" onclick="previewDungeon('${d.id}')">Preview</button>
                    <button class="btn btn-success btn-sm" onclick="approveDungeon('${d.id}','${esc(d.title)}')">Approve</button>
                    <button class="btn btn-danger btn-sm" onclick="rejectDungeon('${d.id}','${esc(d.title)}')">Reject</button>
                </td>
            </tr>`;
        }

        html += '</tbody></table></div>';
        container.innerHTML = html;
    } catch (e) {
        container.innerHTML = '<div style="color:#f87171;text-align:center;padding:20px;">Failed to load dungeons</div>';
        showFeedback('dungeons-feedback', e.message, false);
    }
}

function approveDungeon(id, title) {
    showConfirm(`Approve dungeon "${title}"? This will push the map to the server repo and trigger a deploy.`, async () => {
        const feedback = document.getElementById('dungeons-feedback');
        feedback.style.display = 'none';
        try {
            const data = await apiFetch('/api/dungeons/approve', {
                method: 'POST',
                body: JSON.stringify({ dungeonId: id })
            });
            showFeedback('dungeons-feedback', data.message || 'Approved!', true);
            loadDungeons();
        } catch (e) {
            showFeedback('dungeons-feedback', 'Approve failed: ' + e.message, false);
        }
    });
}

function rejectDungeon(id, title) {
    showConfirm(`Reject dungeon "${title}"?`, async () => {
        try {
            const data = await apiFetch('/api/dungeons/reject', {
                method: 'POST',
                body: JSON.stringify({ dungeonId: id })
            });
            showFeedback('dungeons-feedback', data.message || 'Rejected', true);
            loadDungeons();
        } catch (e) {
            showFeedback('dungeons-feedback', 'Reject failed: ' + e.message, false);
        }
    });
}

function esc(s) {
    if (!s) return '';
    return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;').replace(/'/g,'&#39;');
}

function showFeedback(id, msg, success) {
    const el = document.getElementById(id);
    el.textContent = msg;
    el.className = 'feedback ' + (success ? 'success' : 'error');
    el.style.display = 'block';
}

// ========== Dungeon Preview ==========

async function previewDungeon(id) {
    const overlay = document.getElementById('preview-overlay');
    const content = document.getElementById('preview-content');
    const title = document.getElementById('preview-title');

    title.textContent = 'Loading preview...';
    content.innerHTML = '<div style="text-align:center;padding:40px;color:#888;">Loading...</div>';
    overlay.classList.add('active');

    try {
        const data = await apiFetch(`/api/dungeons/preview/${id}`);
        title.textContent = data.title || 'Untitled Dungeon';
        content.innerHTML = renderPreview(data);
    } catch (e) {
        content.innerHTML = `<div style="color:#f87171;text-align:center;padding:40px;">Failed to load preview: ${esc(e.message)}</div>`;
    }
}

function hidePreview() {
    document.getElementById('preview-overlay').classList.remove('active');
}

function renderPreview(data) {
    let html = '';

    // Description
    if (data.description) {
        html += `<div class="preview-section"><p style="color:#aaa;margin:0;">${esc(data.description)}</p></div>`;
    }

    // Map info + thumbnail
    if (data.map) {
        html += `<div class="preview-section">
            <h4>Map</h4>
            <div class="preview-map-info">
                <div>Size: <span>${data.map.width} x ${data.map.height}</span></div>
                <div>Unique tiles: <span>${data.map.dictEntries}</span></div>
            </div>`;
        if (data.mapThumbnail) {
            html += `<div style="margin-top:10px;text-align:center;overflow:auto;max-height:500px;border:1px solid #333;border-radius:4px;background:#111;">
                <img src="data:image/png;base64,${data.mapThumbnail}"
                     style="image-rendering:pixelated;display:block;margin:0 auto;"
                     title="Map preview — pixel-accurate">
            </div>`;
        }
        html += `</div>`;
    }

    // Custom tiles
    if (data.customTiles && data.customTiles.length > 0) {
        html += `<div class="preview-section"><h4>Custom Tiles (${data.customTiles.length})</h4><div>`;
        for (const t of data.customTiles) {
            html += `<div class="preview-tile" style="background:#${esc(t.hex)};" title="${esc(t.id)}"></div>`;
        }
        html += '</div></div>';
    }

    // Mobs
    if (data.mobs && data.mobs.length > 0) {
        html += `<div class="preview-section"><h4>Mobs (${data.mobs.length})</h4>`;
        for (let i = 0; i < data.mobs.length; i++) {
            const mob = data.mobs[i];
            const spriteScale = mob.spriteSize === 16 ? 64 : 64; // display at 64px
            html += `<div class="preview-entity">`;

            // Sprites
            html += '<div style="display:flex;gap:6px;">';
            if (mob.spriteBase) {
                html += `<div style="text-align:center;"><img src="${mob.spriteBase}" class="preview-sprite" width="${spriteScale}" height="${spriteScale}" title="Base/Idle"><div style="font-size:10px;color:#666;">idle</div></div>`;
            }
            if (mob.spriteAttack) {
                html += `<div style="text-align:center;"><img src="${mob.spriteAttack}" class="preview-sprite" width="${spriteScale}" height="${spriteScale}" title="Attack"><div style="font-size:10px;color:#666;">attack</div></div>`;
            }
            if (!mob.spriteBase && !mob.spriteAttack) {
                html += `<div class="preview-sprite" style="width:${spriteScale}px;height:${spriteScale}px;display:flex;align-items:center;justify-content:center;color:#555;font-size:20px;">?</div>`;
            }
            html += '</div>';

            // Info
            html += `<div class="preview-entity-info">
                <div class="preview-entity-name">${esc(mob.name)}</div>
                <div style="color:#666;font-size:11px;">${mob.spriteSize}x${mob.spriteSize} sprite</div>
                <span class="preview-xml-toggle" onclick="toggleXml(this)">Show XML</span>
                <div class="preview-xml" style="display:none;">${esc(mob.xml)}</div>
            </div>`;

            html += '</div>';
        }
        html += '</div>';
    }

    // Items
    if (data.items && data.items.length > 0) {
        html += `<div class="preview-section"><h4>Items (${data.items.length})</h4>`;
        for (let i = 0; i < data.items.length; i++) {
            const item = data.items[i];
            html += `<div class="preview-entity">`;

            // Sprite
            if (item.sprite) {
                html += `<img src="${item.sprite}" class="preview-sprite" width="64" height="64" title="Item sprite">`;
            } else {
                html += `<div class="preview-sprite" style="width:64px;height:64px;display:flex;align-items:center;justify-content:center;color:#555;font-size:20px;">?</div>`;
            }

            // Info
            html += `<div class="preview-entity-info">
                <div class="preview-entity-name">${esc(item.name)}</div>
                <span class="preview-xml-toggle" onclick="toggleXml(this)">Show XML</span>
                <div class="preview-xml" style="display:none;">${esc(item.xml)}</div>
            </div>`;

            html += '</div>';
        }
        html += '</div>';
    }

    if (!html) {
        html = '<div style="color:#666;text-align:center;padding:40px;">No content to preview</div>';
    }

    return html;
}

function toggleXml(el) {
    const xmlDiv = el.nextElementSibling;
    if (xmlDiv.style.display === 'none') {
        xmlDiv.style.display = 'block';
        el.textContent = 'Hide XML';
    } else {
        xmlDiv.style.display = 'none';
        el.textContent = 'Show XML';
    }
}
