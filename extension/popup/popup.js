const toggle = document.getElementById("lite");
const state = document.getElementById("state");

function show(on) {
  toggle.checked = on;
  state.textContent = on ? "Activado · se recarga la pestaña actual" : "Desactivado";
  state.classList.toggle("on", on);
}

browser.storage.local.get({ lite: false }).then((r) => show(!!r.lite));

toggle.addEventListener("change", async () => {
  const on = toggle.checked;
  await browser.storage.local.set({ lite: on });
  show(on);
  const [tab] = await browser.tabs.query({ active: true, currentWindow: true });
  if (tab && /^https?:/.test(tab.url || "")) browser.tabs.reload(tab.id);
});

const sleep = document.getElementById("sleep");
browser.storage.local.get({ sleepIdleTabs: true }).then((r) => { sleep.checked = !!r.sleepIdleTabs; });
sleep.addEventListener("change", () => browser.storage.local.set({ sleepIdleTabs: sleep.checked }));
