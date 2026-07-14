import { apiFetch } from './api.js';

export async function login(username, password) {
    await apiFetch('/api/admin/login', {
        method: 'POST',
        body: JSON.stringify({ username, password })
    });
    return await checkSession();
}

export async function logout() {
    try {
        await apiFetch('/api/admin/logout', { method: 'POST' });
    } catch {}
    localStorage.removeItem('adminRole');
    localStorage.removeItem('adminPermissions');
    window.location.hash = '#login';
}

export async function checkSession() {
    try {
        const me = await apiFetch('/api/admin/auth/me');
        if (me) {
            localStorage.setItem('adminRole', me.role);
            localStorage.setItem('adminPermissions', JSON.stringify(me.permissions || []));
            return me;
        }
    } catch (e) {
        localStorage.removeItem('adminRole');
        localStorage.removeItem('adminPermissions');
    }
    return null;
}

export function hasPermission(permission) {
    try {
        const perms = JSON.parse(localStorage.getItem('adminPermissions') || '[]');
        return perms.includes(permission);
    } catch {
        return false;
    }
}
