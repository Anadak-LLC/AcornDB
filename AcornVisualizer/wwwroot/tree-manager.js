// Get tree name from URL parameter
const urlParams = new URLSearchParams(window.location.search);
const treeName = urlParams.get('tree');
let currentTreeData = null;
let editingNutId = null;

// Initialize
if (!treeName) {
    window.location.href = '/';
} else {
    refreshData();

    // Setup search
    document.getElementById('searchBox').addEventListener('input', filterNuts);
}

async function refreshData() {
    try {
        const response = await fetch(`/api/TreeData/${treeName}`);

        if (!response.ok) {
            throw new Error(`Failed to load tree: ${response.statusText}`);
        }

        const data = await response.json();
        currentTreeData = data;
        renderTreeHeader(data);
        renderNuts(data.nuts);
    } catch (error) {
        console.error('Failed to load tree data:', error);
        alert('Failed to load tree data: ' + error.message);
    }
}

function renderTreeHeader(data) {
    document.getElementById('treeTitle').textContent = `🌳 ${data.typeName}`;

    const statsHtml = `
        <div class="stat-item">
            <div class="stat-label">Nuts</div>
            <div class="stat-value">${data.nutCount}</div>
        </div>
        <div class="stat-item">
            <div class="stat-label">Stashed</div>
            <div class="stat-value">${data.stats.totalStashed}</div>
        </div>
        <div class="stat-item">
            <div class="stat-label">Tossed</div>
            <div class="stat-value">${data.stats.totalTossed}</div>
        </div>
        <div class="stat-item">
            <div class="stat-label">Squabbles</div>
            <div class="stat-value">${data.stats.squabblesResolved}</div>
        </div>
        <div class="stat-item">
            <div class="stat-label">Trunk</div>
            <div class="stat-value" style="font-size: 16px;">${data.capabilities.trunkType}</div>
        </div>
        <div class="stat-item">
            <div class="stat-label">Features</div>
            <div class="stat-value" style="font-size: 12px;">
                ${data.capabilities.supportsHistory ? '📚 History' : ''}
                ${data.capabilities.supportsSync ? '🔄 Sync' : ''}
                ${data.capabilities.isDurable ? '💾 Durable' : ''}
            </div>
        </div>
    `;

    document.getElementById('treeStats').innerHTML = statsHtml;
}

function renderNuts(nuts) {
    const nutsList = document.getElementById('nutsList');

    if (!nuts || nuts.length === 0) {
        nutsList.innerHTML = '<li class="empty-state">No nuts in this tree. Click "Create Nut" to add one.</li>';
        return;
    }

    const nutsHtml = nuts.map(nut => `
        <li class="nut-item" data-id="${nut.id}">
            <div class="nut-header">
                <div class="nut-id">🌰 ${nut.id}</div>
                <div class="nut-meta">
                    v${nut.version} | ${new Date(nut.timestamp).toLocaleString()}
                </div>
            </div>
            <div class="nut-payload">${nut.payloadJson}</div>
            <div class="nut-actions">
                <button onclick="showEditModal('${nut.id}')" class="btn btn-primary">✏️ Edit</button>
                <button onclick="deleteNut('${nut.id}')" class="btn btn-danger">🗑️ Delete</button>
                ${nut.hasHistory ? `<button onclick="showHistory('${nut.id}')" class="btn btn-secondary">📚 History</button>` : ''}
            </div>
        </li>
    `).join('');

    nutsList.innerHTML = nutsHtml;
}

function filterNuts() {
    const searchTerm = document.getElementById('searchBox').value.toLowerCase();
    const nutItems = document.querySelectorAll('.nut-item');

    nutItems.forEach(item => {
        const id = item.getAttribute('data-id').toLowerCase();
        const visible = id.includes(searchTerm);
        item.style.display = visible ? 'block' : 'none';
    });
}

function showCreateModal() {
    editingNutId = null;
    document.getElementById('modalTitle').textContent = 'Create Nut';
    document.getElementById('nutId').value = '';
    document.getElementById('nutId').disabled = false;
    document.getElementById('nutPayload').value = '';
    document.getElementById('submitBtn').textContent = 'Create';
    document.getElementById('editModal').classList.add('active');
}

function showEditModal(id) {
    const nut = currentTreeData.nuts.find(n => n.id === id);
    if (!nut) return;

    editingNutId = id;
    document.getElementById('modalTitle').textContent = 'Edit Nut';
    document.getElementById('nutId').value = id;
    document.getElementById('nutId').disabled = true;
    document.getElementById('nutPayload').value = nut.payloadJson;
    document.getElementById('submitBtn').textContent = 'Update';
    document.getElementById('editModal').classList.add('active');
}

function closeModal(modalId) {
    document.getElementById(modalId).classList.remove('active');
}

async function submitNut(event) {
    event.preventDefault();

    const id = document.getElementById('nutId').value;
    const payloadJson = document.getElementById('nutPayload').value;

    // Validate JSON
    try {
        JSON.parse(payloadJson);
    } catch (e) {
        alert('Invalid JSON: ' + e.message);
        return;
    }

    const isEdit = editingNutId !== null;
    const url = isEdit
        ? `/api/TreeData/${treeName}/nut/${id}`
        : `/api/TreeData/${treeName}/nut`;

    const method = isEdit ? 'PUT' : 'POST';
    const body = isEdit
        ? { payloadJson }
        : { id, payloadJson };

    try {
        const response = await fetch(url, {
            method: method,
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(body)
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Operation failed');
        }

        closeModal('editModal');
        await refreshData();
        alert(isEdit ? 'Nut updated successfully!' : 'Nut created successfully!');
    } catch (error) {
        console.error('Failed to save nut:', error);
        alert('Failed to save nut: ' + error.message);
    }
}

async function deleteNut(id) {
    if (!confirm(`Are you sure you want to delete nut '${id}'?`)) {
        return;
    }

    try {
        const response = await fetch(`/api/TreeData/${treeName}/nut/${id}`, {
            method: 'DELETE'
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Delete failed');
        }

        await refreshData();
        alert('Nut deleted successfully!');
    } catch (error) {
        console.error('Failed to delete nut:', error);
        alert('Failed to delete nut: ' + error.message);
    }
}

async function showHistory(id) {
    document.getElementById('historyNutId').textContent = id;
    document.getElementById('historyModal').classList.add('active');
    document.getElementById('historyTimeline').innerHTML = '<p>Loading history...</p>';

    try {
        const response = await fetch(`/api/TreeData/${treeName}/nut/${id}/history`);

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Failed to load history');
        }

        const data = await response.json();
        renderHistory(data.history);
    } catch (error) {
        console.error('Failed to load history:', error);
        document.getElementById('historyTimeline').innerHTML =
            `<p style="color: #dc3545;">Failed to load history: ${error.message}</p>`;
    }
}

function renderHistory(history) {
    if (!history || history.length === 0) {
        document.getElementById('historyTimeline').innerHTML =
            '<p>No history available for this nut.</p>';
        return;
    }

    // Sort by version descending (newest first)
    const sortedHistory = [...history].sort((a, b) => b.version - a.version);

    const historyHtml = sortedHistory.map(item => `
        <div class="history-item">
            <div class="history-version">Version ${item.version}</div>
            <div class="history-date">${new Date(item.timestamp).toLocaleString()}</div>
            <div class="nut-payload">${item.payloadJson}</div>
        </div>
    `).join('');

    document.getElementById('historyTimeline').innerHTML = historyHtml;
}

function showRegisterModal() {
    document.getElementById('remoteUrl').value = '';
    document.getElementById('registerModal').classList.add('active');
}

async function submitRegister(event) {
    event.preventDefault();

    const remoteUrl = document.getElementById('remoteUrl').value;

    try {
        const response = await fetch('/api/GroveManagement/register-tree', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                typeName: treeName,
                remoteUrl: remoteUrl
            })
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Registration failed');
        }

        closeModal('registerModal');
        alert('Remote tree registered successfully! Synchronization started.');
    } catch (error) {
        console.error('Failed to register remote tree:', error);
        alert('Failed to register remote tree: ' + error.message);
    }
}

// Auto-refresh every 10 seconds
setInterval(refreshData, 10000);
