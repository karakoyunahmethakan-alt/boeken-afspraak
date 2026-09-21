(function () {
  const TOKEN_KEY = "boeken_admin_token";
  const loginCard = document.getElementById("login-card");
  const panelCard = document.getElementById("panel-card");
  const tbody = document.getElementById("table-body");

  function getToken() { return sessionStorage.getItem(TOKEN_KEY); }
  function setToken(t) { sessionStorage.setItem(TOKEN_KEY, t); }
  function clearToken() { sessionStorage.removeItem(TOKEN_KEY); }

  async function api(path, opts = {}) {
    const headers = Object.assign({}, opts.headers, { Authorization: "Bearer " + getToken() });
    const res = await fetch(path, Object.assign({}, opts, { headers }));
    if (res.status === 401) { clearToken(); showLogin(); throw new Error("unauthorized"); }
    return res;
  }

  function showLogin() { loginCard.style.display = "block"; panelCard.style.display = "none"; }
  function showPanel() { loginCard.style.display = "none"; panelCard.style.display = "block"; loadAppointments(); }

  document.getElementById("login-btn").addEventListener("click", async () => {
    const email = document.getElementById("email").value.trim();
    const password = document.getElementById("password").value;
    const errEl = document.getElementById("login-err");
    errEl.style.display = "none";

    const res = await fetch("/api/admin/login", {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password })
    });
    if (!res.ok) { errEl.textContent = "Onjuiste inloggegevens."; errEl.style.display = "block"; return; }
    const data = await res.json();
    setToken(data.token);
    showPanel();
  });

  document.getElementById("logout-btn").addEventListener("click", () => { clearToken(); showLogin(); });

  document.getElementById("status-filter").addEventListener("change", loadAppointments);
  document.getElementById("export-btn").addEventListener("click", async () => {
    const res = await api("/api/admin/appointments/export.csv");
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url; a.download = "afspraken.csv"; a.click();
    URL.revokeObjectURL(url);
  });

  async function loadAppointments() {
    const status = document.getElementById("status-filter").value;
    const res = await api("/api/admin/appointments" + (status ? "?status=" + status : ""));
    const list = await res.json();
    renderTable(list);
  }

  function renderTable(list) {
    tbody.innerHTML = "";
    list.forEach(a => {
      const tr = document.createElement("tr");
      const statusClass = a.status === "Confirmed" ? "confirmed" : "cancelled";
      const statusLabel = a.status === "Confirmed" ? "Bevestigd" : "Geannuleerd";
      tr.innerHTML = `
        <td>${a.date}</td>
        <td>${a.timeSlot}</td>
        <td><span class="status-tag ${statusClass}">${statusLabel}</span></td>
        <td>${escapeHtml(a.name)}</td>
        <td>${escapeHtml(a.address)}</td>
        <td>${escapeHtml(a.email)}</td>
        <td>${escapeHtml(a.phone || "-")}</td>
        <td>${a.bookCount}</td>
        <td>€ ${a.estimatedPriceEuro.toFixed(2).replace(".", ",")}</td>
        <td>${a.rescheduleCount > 0 ? a.rescheduleCount + "×" : "-"}</td>
        <td></td>
      `;
      if (a.status === "Confirmed") {
        const btn = document.createElement("button");
        btn.className = "btn-secondary";
        btn.textContent = "Annuleren";
        btn.addEventListener("click", async () => {
          if (!confirm(`Afspraak van ${a.name} annuleren?`)) return;
          await api(`/api/admin/appointments/${a.id}/cancel`, { method: "POST" });
          loadAppointments();
        });
        tr.lastElementChild.appendChild(btn);
      }
      tbody.appendChild(tr);
    });
  }

  function escapeHtml(s) {
    const d = document.createElement("div");
    d.textContent = s;
    return d.innerHTML;
  }

  if (getToken()) showPanel(); else showLogin();
})();
