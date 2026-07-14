import { checkSession, hasPermission } from './auth.js';
import * as dashboardView from './views/dashboard.js';
import * as chainsView from './views/chains.js';
import * as chatsView from './views/chats.js';
import * as adminsView from './views/admins.js';
import * as settingsView from './views/settings.js';
import * as auditView from './views/audit.js';

const routes = {
    'dashboard': { render: dashboardView.render, permission: 'Admin.Read' },
    'chains': { render: chainsView.render, permission: 'Admin.Read' },
    'chats': { render: chatsView.render, permission: 'Admin.ManageChains' },
    'admins': { render: adminsView.render, permission: 'Admin.ManageAccounts' },
    'settings': { render: settingsView.render, permission: 'Admin.ManageSettings' },
    'audit': { render: auditView.render, permission: 'Admin.Read' }
};

export async function initRouter() {
    window.addEventListener('hashchange', handleRoute);
    window.addEventListener('auth-required', () => {
        showPage('login-page');
    });

    const me = await checkSession();
    if (me) {
        setupNav();
        handleRoute();
    } else {
        window.location.hash = '';
        showPage('login-page');
    }
}

export function setupNav() {
    document.getElementById('nav-chats').style.display = hasPermission('Admin.ManageChains') ? 'inline-block' : 'none';
    document.getElementById('nav-accounts').style.display = hasPermission('Admin.ManageAccounts') ? 'inline-block' : 'none';
    document.getElementById('nav-settings').style.display = hasPermission('Admin.ManageSettings') ? 'inline-block' : 'none';
    document.getElementById('nav-audit').style.display = hasPermission('Admin.Read') ? 'inline-block' : 'none';
}

export function showPage(pageId) {
    document.querySelectorAll('.page').forEach(el => {
        el.style.display = el.id === pageId ? 'block' : 'none';
    });
}

export async function handleRoute() {
    let hash = window.location.hash.substring(1) || 'dashboard';
    if (hash.startsWith('change-password')) {
        // Change password view is handled via standard template or action
        showPage('admin-page');
        return;
    }

    const me = await checkSession();
    if (!me) {
        showPage('login-page');
        return;
    }

    showPage('admin-page');

    const route = routes[hash];
    if (!route) {
        window.location.hash = '#dashboard';
        return;
    }

    if (route.permission && !hasPermission(route.permission)) {
        const content = document.getElementById('content');
        content.textContent = '';
        const pErr = document.createElement('p');
        pErr.className = 'error';
        pErr.textContent = '您没有访问此页面的权限。';
        content.appendChild(pErr);
        return;
    }

    try {
        await route.render(document.getElementById('content'));
    } catch (e) {
        console.error('Error rendering route: ' + hash, e);
    }
}
