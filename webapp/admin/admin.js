function getCookie(name) {
    const value = `; ${document.cookie}`;
    const parts = value.split(`; ${name}=`);
    if (parts.length === 2) return parts.pop().split(';').shift();
    return null;
}

let isPageInitialized = false;

// Check status on load
checkSessionInit();

async function checkSessionInit() {
    const role = localStorage.getItem('adminRole');
    if (role) {
        showAdminPage();
    }
}

async function apiFetch(url, options = {}) {
    let csrfToken = getCookie('XSRF-TOKEN');
    if (!csrfToken) {
        try {
            await fetch('/api/admin/csrf');
            csrfToken = getCookie('XSRF-TOKEN');
        } catch (e) {
            console.error("Failed to fetch CSRF token", e);
        }
    }

    options.headers = {
        ...options.headers,
        'Content-Type': 'application/json'
    };
    if (csrfToken) {
        options.headers['X-XSRF-TOKEN'] = csrfToken;
    }
    options.credentials = 'include';
    
    const response = await fetch(url, options);
    if (response.status === 401) {
        logout();
        throw new Error('Unauthorized');
    }
    if (!response.ok) {
        const errorData = await response.json().catch(() => ({}));
        throw new Error(errorData.error || 'Request failed');
    }
    return response.json().catch(() => null);
}

async function login() {
    const username = document.getElementById('username').value;
    const password = document.getElementById('password').value;
    const errorEl = document.getElementById('login-error');
    
    try {
        const data = await apiFetch('/api/admin/login', {
            method: 'POST',
            body: JSON.stringify({ username, password })
        });
        
        localStorage.setItem('adminRole', data.role);
        showAdminPage();
    } catch (e) {
        errorEl.textContent = '登录失败: ' + e.message;
    }
}

async function logout() {
    try {
        await apiFetch('/api/admin/logout', { method: 'POST' });
    } catch {}
    localStorage.removeItem('adminRole');
    document.getElementById('login-page').style.display = 'block';
    document.getElementById('admin-page').style.display = 'none';
}

function showAdminPage() {
    document.getElementById('login-page').style.display = 'none';
    document.getElementById('admin-page').style.display = 'block';
    
    const role = localStorage.getItem('adminRole');
    const navAccounts = document.getElementById('nav-accounts');
    if (role === 'RootAdmin') {
        navAccounts.style.display = 'inline-block';
    } else {
        navAccounts.style.display = 'none';
    }
    
    showChains();
}

async function showChains() {
    const content = document.getElementById('content');
    content.innerHTML = '<h2>加载中...</h2>';
    
    const role = localStorage.getItem('adminRole');
    const isAuditor = role === 'AuditorAdmin';

    try {
        const chains = await apiFetch('/api/admin/chains');
        let html = `
            <div class="header-action">
                <h1>所有接龙</h1>
            </div>
            <table>
                <thead>
                    <tr>
                        <th>ID</th>
                        <th>标题</th>
                        <th>创建时间</th>
                        <th>操作</th>
                    </tr>
                </thead>
                <tbody>
        `;
        
        chains.forEach(c => {
            html += `
                <tr>
                    <td>${c.id}</td>
                    <td>${c.title}</td>
                    <td>${new Date(c.createdAt).toLocaleString()}</td>
                    <td>
                        <button onclick="viewMembers(${c.id}, '${c.title.replace(/'/g, "\\'")}')">查看成员</button>
                        ${isAuditor ? '' : `<button class="danger" onclick="deleteChain(${c.id})">删除</button>`}
                    </td>
                </tr>
            `;
        });
        
        html += '</tbody></table>';
        content.innerHTML = html;
    } catch (e) {
        content.innerHTML = `<p class="error">加载失败: ${e.message}</p>`;
    }
}

async function deleteChain(id) {
    if (!confirm('确定要删除这个接龙吗？所有成员数据都将被清除！')) return;
    try {
        await apiFetch(`/api/admin/chains/${id}`, { method: 'DELETE' });
        showChains();
    } catch (e) {
        alert('删除失败: ' + e.message);
    }
}

async function viewMembers(chainId, title) {
    const content = document.getElementById('content');
    content.innerHTML = `<h2>正在获取 [${title}] 的成员...</h2>`;
    
    const role = localStorage.getItem('adminRole');
    const isAuditor = role === 'AuditorAdmin';

    try {
        const members = await apiFetch(`/api/admin/chains/${chainId}/members`);
        let html = `
            <div class="header-action">
                <h1>接龙成员: ${title}</h1>
                <button class="secondary" onclick="showChains()">返回列表</button>
            </div>
            <table>
                <thead>
                    <tr>
                        <th>ID</th>
                        <th>用户名</th>
                        <th>TG 昵称</th>
                        <th>加入时间</th>
                        <th>操作</th>
                    </tr>
                </thead>
                <tbody>
        `;
        
        members.forEach(m => {
            html += `
                <tr>
                    <td>${m.id}</td>
                    <td>${m.username}</td>
                    <td>${m.telegramNickname || '-'}</td>
                    <td>${new Date(m.joinTime).toLocaleString()}</td>
                    <td>
                        ${isAuditor ? '-' : `
                            <button onclick="openEditMember(${m.id}, '${m.username.replace(/'/g, "\\'")}', '${(m.telegramNickname || '').replace(/'/g, "\\'")}')">修改</button>
                            <button class="danger" onclick="deleteMember(${m.id}, ${chainId}, '${title.replace(/'/g, "\\'")}')">删除</button>
                        `}
                    </td>
                </tr>
            `;
        });
        
        html += '</tbody></table>';
        content.innerHTML = html;
    } catch (e) {
        content.innerHTML = `<p class="error">加载失败: ${e.message}</p>`;
    }
}

async function deleteMember(id, chainId, title) {
    if (!confirm('确定要从接龙中移除该成员吗？')) return;
    try {
        await apiFetch(`/api/admin/members/${id}`, { method: 'DELETE' });
        viewMembers(chainId, title);
    } catch (e) {
        alert('删除失败: ' + e.message);
    }
}

function openEditMember(id, username, nickname) {
    document.getElementById('edit-member-id').value = id;
    document.getElementById('edit-username').value = username;
    document.getElementById('edit-nickname').value = nickname;
    document.getElementById('edit-modal').style.display = 'flex';
}

function closeModal() {
    document.getElementById('edit-modal').style.display = 'none';
}

async function saveMember() {
    const id = document.getElementById('edit-member-id').value;
    const username = document.getElementById('edit-username').value;
    const telegramNickname = document.getElementById('edit-nickname').value;
    
    try {
        await apiFetch(`/api/admin/members/${id}`, {
            method: 'PUT',
            body: JSON.stringify({ username, telegramNickname })
        });
        closeModal();
        showChains(); 
    } catch (e) {
        alert('保存失败: ' + e.message);
    }
}

function showChangePassword() {
    const content = document.getElementById('content');
    content.innerHTML = `
        <div class="change-pw-container card">
            <h1>修改管理员密码</h1>
            <div class="form-group">
                <label>旧密码:</label>
                <input type="password" id="old-password">
            </div>
            <div class="form-group">
                <label>新密码:</label>
                <input type="password" id="new-password">
            </div>
            <div class="form-group">
                <label>确认新密码:</label>
                <input type="password" id="confirm-password">
            </div>
            <button onclick="updatePassword()">确认修改</button>
            <p id="pw-error" class="error"></p>
        </div>
    `;
}

async function updatePassword() {
    const oldPassword = document.getElementById('old-password').value;
    const newPassword = document.getElementById('new-password').value;
    const confirmPassword = document.getElementById('confirm-password').value;
    const errorEl = document.getElementById('pw-error');
    
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
        logout();
    } catch (e) {
        errorEl.textContent = '修改失败: ' + e.message;
    }
}

async function showAccounts() {
    const content = document.getElementById('content');
    content.innerHTML = '<h2>加载中...</h2>';
    
    try {
        const accounts = await apiFetch('/api/admin/accounts');
        let html = `
            <div class="header-action">
                <h1>管理员管理</h1>
                <button class="primary" onclick="showCreateAccountForm()">新建管理员</button>
            </div>
            <table>
                <thead>
                    <tr>
                        <th>ID</th>
                        <th>用户名</th>
                        <th>角色</th>
                        <th>是否禁用</th>
                        <th>操作</th>
                    </tr>
                </thead>
                <tbody>
        `;
        
        accounts.forEach(a => {
            html += `
                <tr>
                    <td>${a.id}</td>
                    <td>${a.username}</td>
                    <td>${a.role}</td>
                    <td>${a.isDisabled ? '是' : '否'}</td>
                    <td>
                        <button onclick="showResetPasswordForm(${a.id}, '${a.username}')">重置密码</button>
                        ${a.role === 'RootAdmin' ? '' : `<button class="danger" onclick="deleteAccount(${a.id}, '${a.username}')">删除</button>`}
                    </td>
                </tr>
            `;
        });
        
        html += '</tbody></table>';
        content.innerHTML = html;
    } catch (e) {
        content.innerHTML = `<p class="error">加载失败: ${e.message}</p>`;
    }
}

function showCreateAccountForm() {
    const content = document.getElementById('content');
    content.innerHTML = `
        <div class="change-pw-container card">
            <h1>新建管理员账号</h1>
            <div class="form-group">
                <label>用户名:</label>
                <input type="text" id="new-username">
            </div>
            <div class="form-group">
                <label>密码:</label>
                <input type="password" id="new-password">
            </div>
            <div class="form-group">
                <label>角色:</label>
                <select id="new-role">
                    <option value="OperatorAdmin">OperatorAdmin (运营)</option>
                    <option value="AuditorAdmin">AuditorAdmin (审计)</option>
                </select>
            </div>
            <div class="form-group" style="display: flex; gap: 10px; margin-top: 15px;">
                <button onclick="createAccount()">保存</button>
                <button class="secondary" onclick="showAccounts()">取消</button>
            </div>
            <p id="create-error" class="error"></p>
        </div>
    `;
}

async function createAccount() {
    const username = document.getElementById('new-username').value;
    const password = document.getElementById('new-password').value;
    const role = document.getElementById('new-role').value;
    const errorEl = document.getElementById('create-error');

    try {
        await apiFetch('/api/admin/accounts', {
            method: 'POST',
            body: JSON.stringify({ username, password, role })
        });
        showAccounts();
    } catch (e) {
        errorEl.textContent = '创建失败: ' + e.message;
    }
}

function showResetPasswordForm(id, username) {
    const content = document.getElementById('content');
    content.innerHTML = `
        <div class="change-pw-container card">
            <h1>重置密码: ${username}</h1>
            <input type="hidden" id="reset-id" value="${id}">
            <div class="form-group">
                <label>新密码:</label>
                <input type="password" id="reset-password">
            </div>
            <div class="form-group" style="display: flex; gap: 10px; margin-top: 15px;">
                <button onclick="resetPassword()">确认重置</button>
                <button class="secondary" onclick="showAccounts()">取消</button>
            </div>
            <p id="reset-error" class="error"></p>
        </div>
    `;
}

async function resetPassword() {
    const id = document.getElementById('reset-id').value;
    const password = document.getElementById('reset-password').value;
    const errorEl = document.getElementById('reset-error');

    try {
        await apiFetch(`/api/admin/accounts/${id}/reset-password`, {
            method: 'POST',
            body: JSON.stringify({ password })
        });
        alert('重置密码成功');
        showAccounts();
    } catch (e) {
        errorEl.textContent = '重置失败: ' + e.message;
    }
}

async function deleteAccount(id, username) {
    if (!confirm(`确定要删除管理员 "${username}" 吗？`)) return;
    try {
        await apiFetch(`/api/admin/accounts/${id}`, { method: 'DELETE' });
        showAccounts();
    } catch (e) {
        alert('删除失败: ' + e.message);
    }
}
