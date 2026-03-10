const tg = window.Telegram?.WebApp;
tg?.ready();

const params = new URLSearchParams(window.location.search);
const chainId = Number(params.get("chain_id") || 0);

const titleEl = document.getElementById("title");
const listEl = document.getElementById("memberList");
const joinBtn = document.getElementById("joinBtn");
const statusEl = document.getElementById("status");
const displayNameField = document.getElementById("displayNameField");
const displayNameInput = document.getElementById("displayName");

const user = tg?.initDataUnsafe?.user;
const hasTelegramInitData = Boolean(tg?.initData);

if (user) {
  displayNameInput.value = user.username || user.first_name || `user_${user.id}`;
}

if (!chainId) {
  joinBtn.disabled = true;
  displayNameInput.disabled = true;
  statusEl.textContent = "无法识别接龙信息";
} else if (!hasTelegramInitData) {
  joinBtn.disabled = true;
  displayNameInput.disabled = true;
  displayNameField.hidden = true;
  statusEl.textContent = "当前页面仅用于查看名单。群聊里请点击“私聊填写名字”后加入。";
}

async function loadChain() {
  const resp = await fetch(`/api/chains/${chainId}`);
  if (!resp.ok) {
    statusEl.textContent = "接龙不存在";
    return;
  }

  const data = await resp.json();
  titleEl.textContent = `🍽 ${data.title}`;

  listEl.innerHTML = "";
  if (!data.members?.length) {
    const li = document.createElement("li");
    li.textContent = "";
    listEl.appendChild(li);
    return;
  }

  data.members.forEach((m) => {
    const li = document.createElement("li");
    li.textContent = m.username;
    listEl.appendChild(li);
  });
}

async function joinChain() {
  if (!hasTelegramInitData) {
    statusEl.textContent = "当前页面不在 Telegram WebApp 中，无法识别你的身份。";
    return;
  }

  const displayName = displayNameInput.value.trim();
  if (!displayName) {
    statusEl.textContent = "请先填写显示名字";
    displayNameInput.focus();
    return;
  }

  joinBtn.disabled = true;
  displayNameInput.disabled = true;

  const body = {
    chainId,
    initData: tg.initData,
    displayName: displayName
  };

  const resp = await fetch("/api/join", {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify(body)
  });

  if (resp.status === 401) {
    statusEl.textContent = "身份认证失败，请从 Telegram 重新打开 WebApp。";
    joinBtn.disabled = false;
    displayNameInput.disabled = false;
    return;
  }

  if (!resp.ok) {
    statusEl.textContent = "加入失败";
    joinBtn.disabled = false;
    displayNameInput.disabled = false;
    return;
  }

  const data = await resp.json();
  statusEl.textContent = data.joined ? "加入成功" : "名字已更新";
  await loadChain();
  joinBtn.disabled = false;
  displayNameInput.disabled = false;
}

joinBtn.addEventListener("click", joinChain);
loadChain();
