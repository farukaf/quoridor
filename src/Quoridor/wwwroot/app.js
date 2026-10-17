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
