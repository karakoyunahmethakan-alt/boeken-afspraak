(function () {
  const params = new URLSearchParams(location.search);
  const token = params.get("token");
  const card = document.getElementById("card");

  if (!token) {
    card.innerHTML = '<p class="section-note">Geen geldige link. Controleer of u de volledige link uit uw e-mail heeft geopend.</p>';
    return;
  }

  const statusLabels = { Confirmed: "Bevestigd", Cancelled: "Geannuleerd" };

  async function load() {
    const res = await fetch(`/api/appointments/manage/${token}`);
    if (!res.ok) {
      card.innerHTML = '<p class="section-note">Deze afspraak kon niet worden gevonden.</p>';
      return;
    }
    const a = await res.json();
    render(a);
  }

  function render(a) {
    const dateLabel = new Date(a.date + "T00:00:00").toLocaleDateString("nl-NL", { weekday: "long", day: "numeric", month: "long", year: "numeric" });

    if (a.status === "Cancelled") {
      card.innerHTML = `
        <h2>Geannuleerd</h2>
        <p class="section-note">Deze afspraak (${dateLabel}, ${a.timeSlot}) is geannuleerd. Wilt u een nieuw moment inplannen?</p>
        <a class="submit-btn" style="display:block;text-align:center;text-decoration:none;box-sizing:border-box;" href="/">Nieuwe afspraak maken</a>
      `;
      return;
    }

    card.innerHTML = `
      <h2>${a.name}</h2>
      <p class="section-note">${a.address}</p>
      <p class="section-note"><strong style="color:var(--ink)">${dateLabel} om ${a.timeSlot}</strong><br>
      ${a.bookCount} boeken${a.bookType ? " · " + a.bookType : ""}<br>
      Geschatte prijsindicatie: € ${a.estimatedPriceEuro.toFixed(2).replace(".", ",")}</p>

      <div id="reschedule-block" style="display:none;margin-top:18px;padding-top:18px;border-top:1px solid var(--line);">
        <h2>Nieuw moment kiezen</h2>
        <div class="days" id="days"></div>
        <div class="slots" id="slots"></div>
        <div class="form-err" id="resched-err"></div>
        <button class="submit-btn" id="confirm-resched">Bevestig nieuwe tijd</button>
      </div>

      <div class="form-ok" id="form-ok"></div>
      <div class="form-err" id="form-err"></div>

      <div style="display:flex;gap:10px;margin-top:18px;">
        <button class="btn-secondary" id="btn-resched" style="flex:1;">Verzetten</button>
        <button class="submit-btn danger" id="btn-cancel" style="flex:1;margin-top:0;">Annuleren</button>
      </div>
    `;

    document.getElementById("btn-cancel").addEventListener("click", () => cancelAppointment());
    document.getElementById("btn-resched").addEventListener("click", () => {
      document.getElementById("reschedule-block").style.display = "block";
      loadRescheduleOptions();
    });
  }

  async function cancelAppointment() {
    if (!confirm("Weet u zeker dat u deze afspraak wilt annuleren?")) return;
    const res = await fetch(`/api/appointments/manage/${token}/cancel`, { method: "POST" });
    if (res.ok) load();
    else document.getElementById("form-err").textContent = "Annuleren is niet gelukt, probeer het later opnieuw.";
  }

  // --- Reschedule mini-picker (same shape as the booking page) ---
  let rDays = [], rSelectedDay = 0, rSelectedTime = null, rConfig = null;

  async function loadRescheduleOptions() {
    const cfgRes = await fetch("/api/config");
    rConfig = await cfgRes.json();
    const base = new Date(); base.setHours(0,0,0,0);
    rDays = [];
    for (let i = 0; i < rConfig.daysAhead; i++) { const d = new Date(base); d.setDate(base.getDate()+i); rDays.push(d); }
    renderRDays();
    await renderRSlots();
  }

  function rKey(d) { return d.getFullYear() + "-" + String(d.getMonth()+1).padStart(2,"0") + "-" + String(d.getDate()).padStart(2,"0"); }

  function renderRDays() {
    const el = document.getElementById("days");
    el.innerHTML = "";
    rDays.forEach((d, idx) => {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "day-pill" + (idx === rSelectedDay ? " active" : "");
      btn.innerHTML = `<span class="d">${d.toLocaleDateString("nl-NL",{weekday:"short"})}</span><span>${d.toLocaleDateString("nl-NL",{day:"numeric",month:"short"})}</span>`;
      btn.addEventListener("click", async () => { rSelectedDay = idx; rSelectedTime = null; renderRDays(); await renderRSlots(); });
      el.appendChild(btn);
    });
  }

  async function renderRSlots() {
    const el = document.getElementById("slots");
    el.innerHTML = '<div class="slots-empty">Bezig met laden…</div>';
    const res = await fetch("/api/availability?date=" + encodeURIComponent(rKey(rDays[rSelectedDay])));
    const data = await res.json();
    el.innerHTML = "";
    if (!data.slots.some(s => s.available)) { el.innerHTML = '<div class="slots-empty">Deze dag is volgeboekt.</div>'; return; }
    data.slots.forEach(s => {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "slot" + (rSelectedTime === s.time ? " active" : "");
      btn.textContent = s.time;
      if (!s.available) btn.disabled = true;
      else btn.addEventListener("click", () => { rSelectedTime = s.time; renderRSlots_markActive(); });
      el.appendChild(btn);
    });
  }
  function renderRSlots_markActive() {
    document.querySelectorAll("#slots .slot").forEach(b => b.classList.toggle("active", b.textContent === rSelectedTime));
  }

  document.addEventListener("click", async (e) => {
    if (e.target && e.target.id === "confirm-resched") {
      const err = document.getElementById("resched-err");
      err.style.display = "none";
      if (!rSelectedTime) { err.textContent = "Kies eerst een tijd."; err.style.display = "block"; return; }
      const res = await fetch(`/api/appointments/manage/${token}/reschedule`, {
        method: "POST", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ newDate: rKey(rDays[rSelectedDay]), newTimeSlot: rSelectedTime })
      });
      if (res.status === 409) { err.textContent = "Dit tijdstip is net bezet geraakt."; err.style.display = "block"; await renderRSlots(); return; }
      if (!res.ok) { err.textContent = "Verzetten is niet gelukt."; err.style.display = "block"; return; }
      load();
    }
  });

  load();
})();
