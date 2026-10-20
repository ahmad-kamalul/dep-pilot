'use strict';
const $ = id => document.getElementById(id);
const selected = new Set();
let state = { hosts: [], playbooks: [], jobs: [] };
let csrf;
let refreshing = false;
let ready = false;
let submitting = false;
const date = value => value ? new Date(value).toLocaleString() : '—';
const el = (tag, text, className) => { const node = document.createElement(tag); if (text !== undefined) node.textContent = text; if (className) node.className = className; return node; };
function notice(message, error = false) { $('notice').hidden = false; $('notice').textContent = message; $('notice').className = error ? 'error' : ''; }
async function api(path, body) {
    const response = await fetch('/api/admin/' + path, { method: body === undefined ? 'GET' : 'POST', credentials: 'same-origin', headers: body === undefined ? {} : { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrf }, body: body === undefined ? undefined : JSON.stringify(body) });
    if (response.status === 401) { location.assign('/Login'); throw new Error('Your session has ended.'); }
    if (!response.ok) { const problem = await response.json().catch(() => ({})); throw new Error(problem.detail || 'The request could not be completed.'); }
    return response.status === 204 ? undefined : response.json();
}
function badge(text) { return el('span', text, 'badge ' + text.toLowerCase()); }
function button(label, action, danger = false) { const b = el('button', label, 'button subtle' + (danger ? ' danger' : '')); b.type = 'button'; b.onclick = async () => { b.disabled = true; try { await action(); } catch (e) { notice(e.message, true); } finally { b.disabled = false; } }; return b; }
function updateSelection() {
    for (const id of selected) if (!state.hosts.some(h => h.id === id && !h.revoked && !h.busy)) selected.delete(id);
    $('selected-count').textContent = `${selected.size} host${selected.size === 1 ? '' : 's'} selected`;
    const hint = submitting ? 'Submitting deployment…' : !ready || !csrf ? 'Waiting for connection. Refresh to try again.'
        : !state.playbooks.some(p => p.id === $('playbook').value) ? 'No approved playbook available.'
        : !selected.size ? 'Select an available host to continue.' : !$('deployment-name').value.trim() ? 'Enter a deployment name to continue.' : '';
    $('deploy-hint').textContent = hint;
    $('deploy-button').disabled = !!hint;
}
function cell(...nodes) { const c = el('td'); for (const node of nodes) c.append(node); return c; }
function empty(tbody, message) { const row = el('tr'); const c = el('td', message, 'empty'); c.colSpan = 5; row.append(c); tbody.append(row); }
function render() {
    $('total-hosts').textContent = state.hosts.length;
    $('online-hosts').textContent = state.hosts.filter(h => h.online).length;
    $('active-jobs').textContent = state.jobs.filter(j => ['Pending', 'Running'].includes(j.status)).length;
    $('success-jobs').textContent = state.jobs.filter(j => j.status === 'Succeeded').length;
    const hosts = $('hosts'); hosts.replaceChildren();
    for (const host of state.hosts) {
        const row = el('tr'); const check = el('input'); check.type = 'checkbox'; check.setAttribute('aria-label', 'Select ' + host.hostname); check.disabled = host.revoked || host.busy;
        if (check.disabled) selected.delete(host.id);
        check.checked = selected.has(host.id); check.onchange = () => { check.checked ? selected.add(host.id) : selected.delete(host.id); updateSelection(); };
        const actions = el('div', undefined, 'actions');
        if (!host.revoked) actions.append(button('Revoke', async () => { if (!confirm(`Revoke access for ${host.hostname}? Running workloads may need manual inspection.`)) return; await api(`hosts/${host.id}/revoke`, {}); selected.delete(host.id); notice('Host access revoked.'); await refresh(); }, true));
        actions.append(button('Remove', async () => { if (!confirm(`Deregister ${host.hostname}? Access is revoked; deployment history and application data are retained. Stop this Agent service afterwards.`)) return; await api(`hosts/${host.id}/deregister`, {}); selected.delete(host.id); notice('Host deregistered. Stop its Agent service; application data is retained.'); await refresh(); }, true));
        const m = host.metrics;
        const pct = (used, total) => total > 0 ? (100 * used / total).toFixed(1) + '%' : '—';
        const metrics = m ? `CPU ${m.cpuPercent == null ? 'sampling' : m.cpuPercent.toFixed(1) + '%'} · Memory ${pct(m.memoryUsedBytes, m.memoryTotalBytes)} · Disk ${pct(m.diskTotalBytes - m.diskFreeBytes, m.diskTotalBytes)}` : 'Resource sample pending';
        const status = cell(badge(host.revoked ? 'Revoked' : host.online ? 'Online' : 'Offline'));
        if (host.busy && !host.revoked) {
            const reason = el('small', 'A queued or running job must finish first.'); reason.id = 'busy-' + host.id;
            status.append(badge('Busy'), reason); check.setAttribute('aria-describedby', reason.id);
        }
        row.append(cell(check), cell(el('strong', host.hostname), el('small', host.operatingSystem + ' · Agent ' + host.agentVersion), el('small', host.ipAddress || 'IP pending'), el('small', (host.capabilities || []).join(', '))), status, cell(el('span', date(host.lastSeenAt)), el('small', metrics), el('small', 'Sample: ' + date(m?.sampledAt))), cell(actions)); hosts.append(row);
    }
    if (!state.hosts.length) empty(hosts, 'No hosts registered yet. Start an Agent to connect your first host.');
    const current = $('playbook').value; $('playbook').replaceChildren();
    for (const p of state.playbooks) { const option = el('option', `${p.name} · v${p.version}`); option.value = p.id; $('playbook').append(option); }
    if (state.playbooks.some(p => p.id === current)) $('playbook').value = current;
    const jobs = $('jobs'); jobs.replaceChildren();
    for (const job of state.jobs) {
        const row = el('tr'); const actions = el('div', undefined, 'actions');
        actions.append(button('Logs', async () => {
            const logs = await api(`jobs/${job.id}/logs`);
            $('detail-title').textContent = job.deploymentName + ' / ' + job.hostname;
            $('detail-note').textContent = `${job.status} · Job ${job.id}. Output refreshes when reopened.`;
            $('detail-content').textContent = logs.length ? logs.map(l => l.text).join('\n') : 'No output received yet.';
            $('detail-dialog').showModal();
        }));
        if (['Pending', 'Running'].includes(job.status) && !job.cancelRequested) actions.append(button('Cancel', async () => { await api(`jobs/${job.id}/cancel`, {}); notice('Cancellation requested. Running Agents stop when they renew their lease.'); await refresh(); }));
        row.append(cell(el('strong', job.deploymentName), el('small', job.hostname)), cell(badge(job.status), ...(job.cancelRequested ? [el('small', 'Cancellation requested')] : [])), cell(el('span', date(job.createdAt))), cell(el('small', job.summary || 'Awaiting execution result')), cell(actions)); jobs.append(row);
    }
    if (!state.jobs.length) empty(jobs, 'Your deployment history will appear here.');
    updateSelection();
}
async function refresh() {
    if (refreshing) return;
    refreshing = true;
    try { if (!csrf) csrf = (await api('csrf')).token; state = await api('dashboard'); ready = true; render(); $('sync-status').textContent = 'Updated ' + new Date().toLocaleTimeString(); }
    catch (e) { ready = false; updateSelection(); $('sync-status').textContent = 'Connection interrupted'; notice(e.message, true); }
    finally { refreshing = false; }
}
$('deploy-form').onsubmit = async event => {
    event.preventDefault();
    updateSelection();
    if ($('deploy-button').disabled) return;
    submitting = true; updateSelection();
    try { const result = await api('deployments', { name: $('deployment-name').value.trim(), playbookId: $('playbook').value, hostIds: [...selected] }); selected.clear(); $('deployment-name').value = ''; notice('Deployment queued. Reference: ' + result.id); await refresh(); }
    catch (e) { notice(e.message, true); await refresh(); }
    finally { submitting = false; updateSelection(); }
};
$('deployment-name').oninput = updateSelection;
$('playbook').onchange = updateSelection;
$('refresh').onclick = refresh;
$('close-dialog').onclick = () => $('detail-dialog').close();
$('view-audit').onclick = async () => { try { const entries = await api('audit'); $('detail-title').textContent = 'Audit trail'; $('detail-note').textContent = 'Latest 200 administrative and Agent events.'; $('detail-content').textContent = entries.map(a => `${date(a.createdAt)}  ${a.actor}\n${a.action}  ${a.resource}`).join('\n\n') || 'No events yet.'; $('detail-dialog').showModal(); } catch (e) { notice(e.message, true); } };
refresh();
setInterval(refresh, 5000);
