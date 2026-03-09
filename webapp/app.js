const tg = window.Telegram.WebApp;
tg.ready();

const params = new URLSearchParams(window.location.search);
const chainId = Number(params.get("chain_id") || 0);

const titleEl = document.getElementById("title");
const listEl = document.getElementById("memberList");
const joinBtn = document.getElementById("joinBtn");
const statusEl = document.getElementById("status");

const user = tg.initDataUnsafe?.user;

if (!chainId || !user) {
  joinBtn.disabled = true;
  statusEl.textContent = "无法识别接龙或用户信息";
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
  joinBtn.disabled = true;

  const body = {
    chainId,
    userId: Number(user.id),
    username: user.username || user.first_name || `user_${user.id}`
  };

  const resp = await fetch("/api/join", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-Telegram-Init-Data": tg.initData
    },
    body: JSON.stringify(body)
  });

  if (!resp.ok) {
    statusEl.textContent = "加入失败";
    joinBtn.disabled = false;
    return;
  }

  const data = await resp.json();
  statusEl.textContent = data.joined ? "加入成功" : "你已经参加过了";
  await loadChain();
  joinBtn.disabled = false;
}

joinBtn.addEventListener("click", joinChain);
loadChain();
