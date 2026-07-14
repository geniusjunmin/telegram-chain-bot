function getCookie(name) {
    const value = `; ${document.cookie}`;
    const parts = value.split(`; ${name}=`);
    if (parts.length === 2) return parts.pop().split(';').shift();
    return null;
}

// Check session on load
document.addEventListener("DOMContentLoaded", () => {
    document.getElementById("login-btn").addEventListener("click", login);
    document.getElementById("nav-chains").addEventListener("click", showChains);
    document.getElementById("nav-accounts").addEventListener("click", showAccounts);
    document.getElementById("nav-change-password").addEventListener("click", showChangePassword);
    document.getElementById("nav-logout").addEventListener("click", logout);
    document.getElementById("modal-save-btn").addEventListener("click", saveMember);
    document.getElementById("modal-cancel-btn").addEventListener("click", closeModal);

    checkSessionInit();
});

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
    content.textContent = '';

    const h2 = document.createElement("h2");
    h2.textContent = "加载中...";
    content.appendChild(h2);
    
    const role = localStorage.getItem('adminRole');
    const isAuditor = role === 'AuditorAdmin';

    try {
        const chains = await apiFetch('/api/admin/chains');
        content.textContent = '';

        const headerDiv = document.createElement("div");
        headerDiv.className = "header-action";
        const h1 = document.createElement("h1");
        h1.textContent = "所有接龙";
        headerDiv.appendChild(h1);
        content.appendChild(headerDiv);

        const table = document.createElement("table");
        const thead = document.createElement("thead");
        const headerRow = document.createElement("tr");
        ["ID", "标题", "创建时间", "操作"].forEach(text => {
            const th = document.createElement("th");
            th.textContent = text;
            headerRow.appendChild(th);
        });
        thead.appendChild(headerRow);
        table.appendChild(thead);

        const tbody = document.createElement("tbody");
        chains.forEach(c => {
            const row = document.createElement("tr");

            const tdId = document.createElement("td");
            tdId.textContent = c.id;
            row.appendChild(tdId);

            const tdTitle = document.createElement("td");
            tdTitle.textContent = c.title;
            row.appendChild(tdTitle);

            const tdTime = document.createElement("td");
            tdTime.textContent = new Date(c.createdAt).toLocaleString();
            row.appendChild(tdTime);

            const tdActions = document.createElement("td");
            
            const btnView = document.createElement("button");
            btnView.textContent = "查看成员";
            btnView.addEventListener("click", () => viewMembers(c.id, c.title));
            tdActions.appendChild(btnView);

            if (!isAuditor) {
                const btnDelete = document.createElement("button");
                btnDelete.className = "danger";
                btnDelete.textContent = "删除";
                btnDelete.addEventListener("click", () => deleteChain(c.id));
                tdActions.appendChild(btnDelete);
            }

            row.appendChild(tdActions);
            tbody.appendChild(row);
        });

        table.appendChild(tbody);
        content.appendChild(table);
    } catch (e) {
        const pErr = document.createElement("p");
        pErr.className = "error";
        pErr.textContent = `加载失败: ${e.message}`;
        content.appendChild(pErr);
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
    content.textContent = '';

    const h2 = document.createElement("h2");
    h2.textContent = `正在获取 [${title}] 的成员...`;
    content.appendChild(h2);
    
    const role = localStorage.getItem('adminRole');
    const isAuditor = role === 'AuditorAdmin';

    try {
        const members = await apiFetch(`/api/admin/chains/${chainId}/members`);
        content.textContent = '';

        const headerDiv = document.createElement("div");
        headerDiv.className = "header-action";
        const h1 = document.createElement("h1");
        h1.textContent = `接龙成员: ${title}`;
        headerDiv.appendChild(h1);

        const btnBack = document.createElement("button");
        btnBack.className = "secondary";
        btnBack.textContent = "返回列表";
        btnBack.addEventListener("click", showChains);
        headerDiv.appendChild(btnBack);
        content.appendChild(headerDiv);

        const table = document.createElement("table");
        const thead = document.createElement("thead");
        const headerRow = document.createElement("tr");
        ["ID", "用户名", "TG 昵称", "加入时间", "操作"].forEach(text => {
            const th = document.createElement("th");
            th.textContent = text;
            headerRow.appendChild(th);
        });
        thead.appendChild(headerRow);
        table.appendChild(thead);

        const tbody = document.createElement("tbody");
        members.forEach(m => {
            const row = document.createElement("tr");

            const tdId = document.createElement("td");
            tdId.textContent = m.id;
            row.appendChild(tdId);

            const tdUser = document.createElement("td");
            tdUser.textContent = m.displayName;
            row.appendChild(tdUser);

            const tdNick = document.createElement("td");
            tdNick.textContent = m.telegramUsername || '-';
            row.appendChild(tdNick);

            const tdTime = document.createElement("td");
            tdTime.textContent = new Date(m.joinedAt).toLocaleString();
            row.appendChild(tdTime);

            const tdActions = document.createElement("td");
            if (isAuditor) {
                tdActions.textContent = '-';
            } else {
                const btnEdit = document.createElement("button");
                btnEdit.textContent = "修改";
                btnEdit.addEventListener("click", () => openEditMember(m.id, m.displayName, m.telegramUsername || ''));
                tdActions.appendChild(btnEdit);

                const btnDel = document.createElement("button");
                btnDel.className = "danger";
                btnDel.textContent = "删除";
                btnDel.addEventListener("click", () => deleteMember(m.id, chainId, title));
                tdActions.appendChild(btnDel);
            }

            row.appendChild(tdActions);
            tbody.appendChild(row);
        });

        table.appendChild(tbody);
        content.appendChild(table);
    } catch (e) {
        const pErr = document.createElement("p");
        pErr.className = "error";
        pErr.textContent = `加载失败: ${e.message}`;
        content.appendChild(pErr);
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
    content.textContent = '';

    const card = document.createElement("div");
    card.className = "change-pw-container card";

    const h1 = document.createElement("h1");
    h1.textContent = "修改管理员密码";
    card.appendChild(h1);

    const fgOld = document.createElement("div");
    fgOld.className = "form-group";
    const lblOld = document.createElement("label");
    lblOld.textContent = "旧密码:";
    const inputOld = document.createElement("input");
    inputOld.type = "password";
    inputOld.id = "old-password";
    fgOld.appendChild(lblOld);
    fgOld.appendChild(inputOld);
    card.appendChild(fgOld);

    const fgNew = document.createElement("div");
    fgNew.className = "form-group";
    const lblNew = document.createElement("label");
    lblNew.textContent = "新密码:";
    const inputNew = document.createElement("input");
    inputNew.type = "password";
    inputNew.id = "new-password";
    fgNew.appendChild(lblNew);
    fgNew.appendChild(inputNew);
    card.appendChild(fgNew);

    const fgConf = document.createElement("div");
    fgConf.className = "form-group";
    const lblConf = document.createElement("label");
    lblConf.textContent = "确认新密码:";
    const inputConf = document.createElement("input");
    inputConf.type = "password";
    inputConf.id = "confirm-password";
    fgConf.appendChild(lblConf);
    fgConf.appendChild(inputConf);
    card.appendChild(fgConf);

    const btnSubmit = document.createElement("button");
    btnSubmit.textContent = "确认修改";
    btnSubmit.addEventListener("click", updatePassword);
    card.appendChild(btnSubmit);

    const errorEl = document.createElement("p");
    errorEl.id = "pw-error";
    errorEl.className = "error";
    card.appendChild(errorEl);

    content.appendChild(card);
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
    content.textContent = '';

    const h2 = document.createElement("h2");
    h2.textContent = "加载中...";
    content.appendChild(h2);
    
    try {
        const accounts = await apiFetch('/api/admin/accounts');
        content.textContent = '';

        const headerDiv = document.createElement("div");
        headerDiv.className = "header-action";
        const h1 = document.createElement("h1");
        h1.textContent = "管理员管理";
        headerDiv.appendChild(h1);

        const btnCreate = document.createElement("button");
        btnCreate.className = "primary";
        btnCreate.textContent = "新建管理员";
        btnCreate.addEventListener("click", showCreateAccountForm);
        headerDiv.appendChild(btnCreate);
        content.appendChild(headerDiv);

        const table = document.createElement("table");
        const thead = document.createElement("thead");
        const headerRow = document.createElement("tr");
        ["ID", "用户名", "角色", "是否禁用", "操作"].forEach(text => {
            const th = document.createElement("th");
            th.textContent = text;
            headerRow.appendChild(th);
        });
        thead.appendChild(headerRow);
        table.appendChild(thead);

        const tbody = document.createElement("tbody");
        accounts.forEach(a => {
            const row = document.createElement("tr");

            const tdId = document.createElement("td");
            tdId.textContent = a.id;
            row.appendChild(tdId);

            const tdUser = document.createElement("td");
            tdUser.textContent = a.username;
            row.appendChild(tdUser);

            const tdRole = document.createElement("td");
            tdRole.textContent = a.role;
            row.appendChild(tdRole);

            const tdDisabled = document.createElement("td");
            tdDisabled.textContent = a.isDisabled ? '是' : '否';
            row.appendChild(tdDisabled);

            const tdActions = document.createElement("td");
            
            const btnReset = document.createElement("button");
            btnReset.textContent = "重置密码";
            btnReset.addEventListener("click", () => showResetPasswordForm(a.id, a.username));
            tdActions.appendChild(btnReset);

            if (a.role !== 'RootAdmin') {
                const btnDel = document.createElement("button");
                btnDel.className = "danger";
                btnDel.textContent = "删除";
                btnDel.addEventListener("click", () => deleteAccount(a.id, a.username));
                tdActions.appendChild(btnDel);
            }

            row.appendChild(tdActions);
            tbody.appendChild(row);
        });

        table.appendChild(tbody);
        content.appendChild(table);
    } catch (e) {
        const pErr = document.createElement("p");
        pErr.className = "error";
        pErr.textContent = `加载失败: ${e.message}`;
        content.appendChild(pErr);
    }
}

function showCreateAccountForm() {
    const content = document.getElementById('content');
    content.textContent = '';

    const card = document.createElement("div");
    card.className = "change-pw-container card";

    const h1 = document.createElement("h1");
    h1.textContent = "新建管理员账号";
    card.appendChild(h1);

    const fgUser = document.createElement("div");
    fgUser.className = "form-group";
    const lblUser = document.createElement("label");
    lblUser.textContent = "用户名:";
    const inputUser = document.createElement("input");
    inputUser.type = "text";
    inputUser.id = "new-username";
    fgUser.appendChild(lblUser);
    fgUser.appendChild(inputUser);
    card.appendChild(fgUser);

    const fgPass = document.createElement("div");
    fgPass.className = "form-group";
    const lblPass = document.createElement("label");
    lblPass.textContent = "密码:";
    const inputPass = document.createElement("input");
    inputPass.type = "password";
    inputPass.id = "new-password";
    fgPass.appendChild(lblPass);
    fgPass.appendChild(inputPass);
    card.appendChild(fgPass);

    const fgRole = document.createElement("div");
    fgRole.className = "form-group";
    const lblRole = document.createElement("label");
    lblRole.textContent = "角色:";
    const selectRole = document.createElement("select");
    selectRole.id = "new-role";

    const optOp = document.createElement("option");
    optOp.value = "OperatorAdmin";
    optOp.textContent = "OperatorAdmin (运营)";
    selectRole.appendChild(optOp);

    const optAud = document.createElement("option");
    optAud.value = "AuditorAdmin";
    optAud.textContent = "AuditorAdmin (审计)";
    selectRole.appendChild(optAud);

    fgRole.appendChild(lblRole);
    fgRole.appendChild(selectRole);
    card.appendChild(fgRole);

    const fgActions = document.createElement("div");
    fgActions.className = "form-group";
    fgActions.style.display = "flex";
    fgActions.style.gap = "10px";
    fgActions.style.marginTop = "15px";

    const btnSave = document.createElement("button");
    btnSave.textContent = "保存";
    btnSave.addEventListener("click", createAccount);
    fgActions.appendChild(btnSave);

    const btnCancel = document.createElement("button");
    btnCancel.className = "secondary";
    btnCancel.textContent = "取消";
    btnCancel.addEventListener("click", showAccounts);
    fgActions.appendChild(btnCancel);

    card.appendChild(fgActions);

    const errorEl = document.createElement("p");
    errorEl.id = "create-error";
    errorEl.className = "error";
    card.appendChild(errorEl);

    content.appendChild(card);
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
    content.textContent = '';

    const card = document.createElement("div");
    card.className = "change-pw-container card";

    const h1 = document.createElement("h1");
    h1.textContent = `重置密码: ${username}`;
    card.appendChild(h1);

    const inputId = document.createElement("input");
    inputId.type = "hidden";
    inputId.id = "reset-id";
    inputId.value = id;
    card.appendChild(inputId);

    const fgPass = document.createElement("div");
    fgPass.className = "form-group";
    const lblPass = document.createElement("label");
    lblPass.textContent = "新密码:";
    const inputPass = document.createElement("input");
    inputPass.type = "password";
    inputPass.id = "reset-password";
    fgPass.appendChild(lblPass);
    fgPass.appendChild(inputPass);
    card.appendChild(fgPass);

    const fgActions = document.createElement("div");
    fgActions.className = "form-group";
    fgActions.style.display = "flex";
    fgActions.style.gap = "10px";
    fgActions.style.marginTop = "15px";

    const btnReset = document.createElement("button");
    btnReset.textContent = "确认重置";
    btnReset.addEventListener("click", resetPassword);
    fgActions.appendChild(btnReset);

    const btnCancel = document.createElement("button");
    btnCancel.className = "secondary";
    btnCancel.textContent = "取消";
    btnCancel.addEventListener("click", showAccounts);
    fgActions.appendChild(btnCancel);

    card.appendChild(fgActions);

    const errorEl = document.createElement("p");
    errorEl.id = "reset-error";
    errorEl.className = "error";
    card.appendChild(errorEl);

    content.appendChild(card);
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
