function GetTimezone() {
    return Intl.DateTimeFormat().resolvedOptions().timeZone;
}
// Legacy names are suggestions only; legacy IDs never establish player identity.
function GetLegacyName() {
    try {
        const legacy = JSON.parse(localStorage.getItem('USER_KEY'));
        const name = legacy?.userName ?? legacy?.UserName;
        return typeof name === 'string' ? name.slice(0, 40) : '';
    } catch { return ''; }
}

async function CopyRoomLink(url) {
    await navigator.clipboard.writeText(url);
}
function FocusBoardControl(kind) {
    const selector = kind === 'wall' ? '.board [data-corner="1,0"]' : '.board [data-current-pawn="true"]';
    const target = document.querySelector(selector);
    if (target) target.focus();
}
// Keep one board control in the tab sequence. Native buttons handle Enter/Space.
document.addEventListener('focusin', event => {
    if (!event.target.matches('.board-control')) return;
    const board = event.target.closest('.board');
    board.querySelectorAll('.board-control').forEach(control => control.tabIndex = control === event.target ? 0 : -1);
});
document.addEventListener('keydown', event => {
    const current = event.target;
    if (!current.matches('.board-control')) return;
    const direction = { ArrowUp: [-1, 0], ArrowDown: [1, 0], ArrowLeft: [0, -1], ArrowRight: [0, 1] }[event.key];
    if (!direction) return;
    event.preventDefault();
    const row = Number(current.dataset.row), column = Number(current.dataset.column);
    const controls = [...current.closest('.board').querySelectorAll('.board-control')].filter(control => !control.disabled);
    const [vertical, horizontal] = direction;
    const candidates = controls.map(control => ({ control, dr: Number(control.dataset.row) - row, dc: Number(control.dataset.column) - column }))
        .filter(item => vertical ? item.dr * vertical > 0 : item.dc * horizontal > 0);
    // Prefer the same row/column; at an edge the closest point in that direction is reachable.
    candidates.sort((a,b) => vertical ? Math.abs(a.dc) - Math.abs(b.dc) || Math.abs(a.dr) - Math.abs(b.dr) : Math.abs(a.dr) - Math.abs(b.dr) || Math.abs(a.dc) - Math.abs(b.dc));
    candidates[0]?.control.focus();
});
// Pages restored from the browser's back-forward cache retain a dead server circuit.
// Reload to establish a fresh interactive circuit before accepting player input.
window.addEventListener('pageshow', event => {
    if (event.persisted) window.location.reload();
});

document.addEventListener('components-reconnect-state-changed', event => {
    if (event.detail?.state === 'rejected') window.location.reload();
});
