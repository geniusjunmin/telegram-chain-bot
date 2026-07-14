import { login, logout } from './js/auth.js';
import { initRouter, showPage, handleRoute, setupNav } from './js/router.js';
import { el } from './js/dom.js';
import { apiFetch } from './js/api.js';

document.addEventListener('DOMContentLoaded', async () => {
    // 1. Bind Login
    const loginBtn = document.getElementById('login-btn');
    if (loginBtn) {
        loginBtn.addEventListener('click', handleLogin);
    }

    // 2. Bind Nav
    const navMapping = {
        'nav-dashboard': '#dashboard',
        'nav-chains': '#chains',
        'nav-chats': '#chats',
        'nav-accounts': '#admins',
        'nav-settings': '#settings',
        'nav-audit': '#audit',
        'nav-change-password': '#change-password'
    };

    for (const [id, hash] of Object.entries(navMapping)) {
        const btn = document.getElementById(id);
        if (btn) {
            btn.addEventListener('click', () => {
                window.location.hash = hash;
            });
        }
    }

    const logoutBtn = document.getElementById('nav-logout');
    if (logoutBtn) {
        logoutBtn.addEventListener('click', handleLogout);
    }

    // 3. Init Router
    await initRouter();

    // Check if change password hash
    window.addEventListener('hashchange', checkHashChangePassword);
    checkHashChangePassword();
});

async function handleLogin() {
    const userVal = document.getElementById('username').value.trim();
    const passVal = document.getElementById('password').value;
    const errorEl = document.getElementById('login-error');
    errorEl.textContent = '';

    try {
        const me = await login(userVal, passVal);
        if (me) {
            setupNav();
            if (me.mustChangePassword) {
                window.location.hash = '#change-password';
            } else {
                window.location.hash = '#dashboard';
            }
            await handleRoute();
        }
    } catch (e) {
        errorEl.textContent = '登录失败: ' + e.message;
    }
}

async function handleLogout() {
    await logout();
}

function checkHashChangePassword() {
    if (window.location.hash === '#change-password') {
        renderChangePassword();
    }
}

function renderChangePassword() {
    const content = document.getElementById('content');
    content.textContent = '';

    const inputOld = el('input', { type: 'password' });
    const inputNew = el('input', { type: 'password' });
    const inputConfirm = el('input', { type: 'password' });
    const errorEl = el('p', { className: 'error' });

    const btnSubmit = el('button', {
        onclick: async () => {
            const oldPassword = inputOld.value;
            const newPassword = inputNew.value;
            const confirmPassword = inputConfirm.value;
            errorEl.textContent = '';

            if (newPassword !== confirmPassword) {
                errorEl.textContent = '两次输入的新密码不一致';
                return;
            }

            try {
                await apiFetch('/api/admin/change-password', {
                    method: 'POST',
                    body: JSON.stringify({ oldPassword, newPassword })
                });
                alert('密码修改成功，请重新登录');
                await logout();
            } catch (e) {
                errorEl.textContent = '修改失败: ' + e.message;
            }
        }
    }, '确认修改');

    const card = el('div', { className: 'change-pw-container card' },
        el('h1', {}, '修改管理员密码'),
        el('div', { className: 'form-group' },
            el('label', {}, '旧密码:'),
            inputOld
        ),
        el('div', { className: 'form-group' },
            el('label', {}, '新密码:'),
            inputNew
        ),
        el('div', { className: 'form-group' },
            el('label', {}, '确认新密码:'),
            inputConfirm
        ),
        btnSubmit,
        errorEl
    );

    content.appendChild(card);
}
