// AcornDB Canopy Visualizer - JavaScript

let currentGraph = null;
let selectedTree = null;

async function refreshData() {
    try {
        const response = await fetch('/api/GroveGraph');
        const data = await response.json();
        currentGraph = data;

        updateStats(data.stats);
        updateTreesList(data.trees);

    } catch (error) {
        console.error('Failed to load data:', error);
    }
}

function updateStats(stats) {
    document.getElementById('totalTrees').textContent = stats.totalTrees || 0;
    document.getElementById('totalNuts').textContent = stats.totalNuts || 0;
    document.getElementById('totalStashed').textContent = stats.totalStashed || 0;
    document.getElementById('totalTossed').textContent = stats.totalTossed || 0;
    document.getElementById('totalSquabbles').textContent = stats.totalSquabbles || 0;
}

function updateTreesList(trees) {
    const container = document.getElementById('treesList');
    container.innerHTML = '';

    if (!trees || trees.length === 0) {
        container.innerHTML = '<p class="placeholder">No trees in grove</p>';
        return;
    }

    trees.forEach(tree => {
        const card = document.createElement('div');
        card.className = 'tree-card';
        card.onclick = () => selectTree(tree.id);

        const icon = tree.supportsHistory ? '📚' : '🌳';
        const durableIcon = tree.isDurable ? '💾' : '⚡';

        card.innerHTML = `
            <div class="tree-card-header">
                <span class="tree-icon">${icon}</span>
                <span class="tree-type">${tree.typeName}</span>
            </div>
            <div class="tree-card-body">
                <div class="tree-stat">
                    <span class="label">Nuts:</span>
                    <span class="value">${tree.nutCount}</span>
                </div>
                <div class="tree-stat">
                    <span class="label">Trunk:</span>
                    <span class="value">${tree.trunkType}</span>
                </div>
                <div class="tree-badges">
                    <span class="badge" title="${tree.isDurable ? 'Durable' : 'In-Memory'}">${durableIcon}</span>
                    ${tree.supportsHistory ? '<span class="badge" title="Has History">📜</span>' : ''}
                </div>
            </div>
            <div class="tree-card-footer">
                <a href="/TreeManager?tree=${tree.id}" class="btn-manage" onclick="event.stopPropagation()">⚙️ Manage</a>
            </div>
        `;

        container.appendChild(card);
    });
}

async function selectTree(treeId) {
    selectedTree = treeId;

    try {
        const response = await fetch(`/api/TreeData/${treeId}`);
        const data = await response.json();

        displayTreeDetail(data);
    } catch (error) {
        console.error('Failed to load tree details:', error);
    }
}

function displayTreeDetail(tree) {
    const titleElem = document.getElementById('detailTitle');
    const detailElem = document.getElementById('treeDetail');

    titleElem.textContent = `🌳 ${tree.typeName}`;

    let html = `
        <div class="tree-detail-header">
            <h3>Trunk: ${tree.capabilities.trunkType}</h3>
            <div class="capability-badges">
                ${tree.capabilities.supportsHistory ? '<span class="badge">📜 History</span>' : ''}
                ${tree.capabilities.supportsSync ? '<span class="badge">🔄 Sync</span>' : ''}
                ${tree.capabilities.isDurable ? '<span class="badge">💾 Durable</span>' : ''}
                ${tree.capabilities.supportsAsync ? '<span class="badge">⚡ Async</span>' : ''}
            </div>
        </div>

        <div class="stats-grid">
            <div class="stat-item">
                <span class="stat-label">Total Stashed:</span>
                <span class="stat-value">${tree.stats.totalStashed}</span>
            </div>
            <div class="stat-item">
                <span class="stat-label">Total Tossed:</span>
                <span class="stat-value">${tree.stats.totalTossed}</span>
            </div>
            <div class="stat-item">
                <span class="stat-label">Squabbles:</span>
                <span class="stat-value">${tree.stats.squabblesResolved}</span>
            </div>
            <div class="stat-item">
                <span class="stat-label">Active Tangles:</span>
                <span class="stat-value">${tree.stats.activeTangles}</span>
            </div>
        </div>

        <h4>Nuts (${tree.nutCount})</h4>
        <div class="nuts-list">
    `;

    if (tree.nuts && tree.nuts.length > 0) {
        tree.nuts.forEach(nut => {
            const timestamp = new Date(nut.timestamp).toLocaleString();
            html += `
                <div class="nut-card">
                    <div class="nut-header">
                        <strong>🌰 ${nut.id}</strong>
                        <span class="nut-timestamp">${timestamp}</span>
                    </div>
                    <div class="nut-payload">
                        <pre>${nut.payloadJson}</pre>
                    </div>
                    ${nut.hasHistory ? '<div class="nut-badge">📜 Has History</div>' : ''}
                </div>
            `;
        });
    } else {
        html += '<p class="placeholder">No nuts in this tree</p>';
    }

    html += '</div>';

    detailElem.innerHTML = html;
}
