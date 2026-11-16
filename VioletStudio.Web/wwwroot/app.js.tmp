const $ = (s) => document.querySelector(s);
const $$ = (s) => [...document.querySelectorAll(s)];
const state = { mode: 'video', quality: '720p', height: 720, availableQualities: null, pro: false, isAdmin: false, pendingLicense: '', reward: 0, downloads: [], free: 3, library: false };
const modalLayer = $('#modalLayer'), modalContent = $('#modalContent');
let resolveTimer = 0, resolvingUrl = '';
const defaultQualityMarkup = $('#qualityOptions').innerHTML;

function toast(message) { const old = $('.toast'); if (old) old.remove(); const el = document.createElement('div'); el.className = 'toast'; el.textContent = message; document.body.append(el); setTimeout(() => el.remove(), 3200); }
function showModal(html) { modalContent.innerHTML = html; modalLayer.classList.remove('hidden'); $('#modalClose').focus(); }
function closeModal() { modalLayer.classList.add('hidden'); modalContent.innerHTML = ''; }
function updatePlan() {
  $$('.quality-card').forEach(card => { const locked = Number(card.dataset.height || ({'4K':2160,'2K':1440,'1080p':1080}[card.dataset.quality] || 0)) > 720; card.classList.toggle('locked', locked && !state.pro); const first = card.querySelector('span'); if (!first) return; const label = card.dataset.quality; if (locked && !state.pro) { first.innerHTML = `${label} <i>✦ PRO</i>`; card.querySelector('.lock')?.classList.remove('hidden'); } else { first.textContent = label + (label === '720p' ? '  ÜCRETSİZ' : ''); card.querySelector('.lock')?.classList.add('hidden'); } });
  $('#adProgressText').textContent = `${state.reward} / 4 reklam izlendi`;
  $$('.ad-dots i').forEach((dot, i) => dot.classList.toggle('done', i < state.reward));
  if ($('#planBadge')) $('#planBadge').textContent = state.isAdmin ? 'ADMIN · SINIRSIZ PRO' : (state.pro ? 'PRO' : 'STANDART');
  if ($('#planName')) $('#planName').textContent = state.isAdmin ? 'Yönetici · Sınırsız Pro' : (state.pro ? 'Pro plan' : 'Standart plan');
  if ($('#planDetail')) $('#planDetail').textContent = state.isAdmin ? 'Tüm kalite seçenekleri açık' : (state.pro ? 'Pro erişimi etkin' : '720p ve altı ücretsiz');
  if ($('#planQuality')) $('#planQuality').innerHTML = state.isAdmin ? 'Tüm çözünürlükler' : (state.pro ? '4K <small>ve altı</small>' : '720p <small>ve altı</small>');
  if ($('#planCaption')) $('#planCaption').textContent = state.isAdmin ? 'Admin hesabında sınırsız Pro erişimi' : (state.pro ? 'Pro planında kullanılabilir' : 'Standart planda kullanılabilir');
  if ($('#watchAdsButton')) $('#watchAdsButton').classList.toggle('hidden', state.isAdmin);
  if ($('.ad-progress')) $('.ad-progress').classList.toggle('hidden', state.isAdmin);
}
function setQuality(q, height) { state.quality = q; state.height = Number(height) || 720; $$('.quality-card').forEach(c => { const selected = Number(c.dataset.height || 0) === state.height; c.classList.toggle('selected', selected); const mark = c.querySelector('.check'); if (mark) mark.remove(); if (selected && !c.classList.contains('locked')) c.insertAdjacentHTML('beforeend', '<span class="check">✓</span>'); }); }
function renderQualities(qualities, errorMessage = '') {
  state.availableQualities = qualities;
  const grid = $('#qualityOptions');
  if (!Array.isArray(qualities) || qualities.length === 0) { grid.innerHTML = '<div class="quality-empty"></div>'; grid.firstElementChild.textContent=errorMessage||'Bu bağlantı için çözünürlük bilgisi alınamadı.'; return; }
  const tiers = new Map();
  for (const item of qualities) { const h=Number(item.height); if(!Number.isFinite(h)||h<1) continue; const label=h>=2160?'4K':h>=1440?'2K':`${h}p`; const previous=tiers.get(label); if(!previous||h>previous.height)tiers.set(label,{label,height:h}); }
  const options=[...tiers.values()].sort((a,b)=>b.height-a.height);
  if (!options.length) { grid.innerHTML = '<div class="quality-empty">Bu video için video çözünürlüğü bulunamadı.</div>'; return; }
  grid.innerHTML=options.map(({label,height})=>`<button class="quality-card${height===state.height?' selected':''}" data-quality="${label}" data-height="${height}"><span>${label}</span><small>${height}p${height===720?' · HD':''}</small>${height===state.height?'<span class="check">✓</span>':''}</button>`).join('');
  if(!options.some(o=>o.height===state.height)){const free=options.find(o=>o.height<=720)||options.at(-1);state.quality=free.label;state.height=free.height;setQuality(state.quality,state.height);}
  updatePlan();
}
function parseLink(raw) { try { const u = new URL(raw.trim()); if (!['http:','https:'].includes(u.protocol)) return null; return u; } catch { return null; } }
function fallbackVideoTitle(u) { const host=u.hostname.replace(/^www\./,''); if(/youtu\.be|youtube\.com/.test(host))return 'YouTube videosu';if(/tiktok\.com/.test(host))return 'TikTok videosu';if(/instagram\.com/.test(host))return 'Instagram gönderisi';if(/x\.com|twitter\.com/.test(host))return 'X videosu';let tail=u.pathname.split('/').filter(Boolean).pop()||'Video bağlantısı';try{tail=decodeURIComponent(tail);}catch{}return tail.replace(/[-_]/g,' '); }
function inspectLink() {
  const u = parseLink($('#urlInput').value); const preview = $('#videoPreview');
  if (!u) { preview.classList.add('hidden'); resolvingUrl=''; state.availableQualities=null;state.quality='720p';state.height=720;$('#qualityOptions').innerHTML=defaultQualityMarkup;updatePlan();clearTimeout(resolveTimer); $('#previewImage').classList.add('hidden'); $('#previewPlay').classList.remove('hidden'); return; }
  if (resolvingUrl === u.href) return;
  resolvingUrl=u.href;state.availableQualities=null;$('#qualityOptions').innerHTML='<div class="quality-empty">Videonun kaliteleri aranıyor…</div>';$('#previewHost').textContent='Video bilgisi yükleniyor…'; $('#previewTitle').textContent=fallbackVideoTitle(u); $('#previewProvider').textContent='VİDEO BAĞLANTISI'; $('#previewImage').classList.add('hidden'); $('#previewPlay').classList.remove('hidden'); preview.classList.remove('hidden'); clearTimeout(resolveTimer);
  resolveTimer=setTimeout(async()=>{try{const r=await fetch(`/api/video/resolve?url=${encodeURIComponent(u.href)}`,{credentials:'same-origin'});if(!r.ok)throw new Error('metadata');const data=await r.json();if(resolvingUrl!==u.href)return;renderQualities(data.qualities,data.formatError);$('#previewTitle').textContent=data.title||fallbackVideoTitle(u);$('#previewHost').textContent=data.author?`${data.author} · ${u.hostname.replace(/^www\./,'')}`:u.hostname.replace(/^www\./,'');$('#previewProvider').textContent=(data.provider||'VIDEO')+' · KAPAK';if(data.thumbnailUrl){const img=$('#previewImage');img.onload=()=>{img.classList.remove('hidden');$('#previewPlay').classList.add('hidden');};img.onerror=()=>{img.classList.add('hidden');$('#previewPlay').classList.remove('hidden');};img.src=data.thumbnailUrl;}else{$('#previewHost').textContent=data.author?`${data.author} · ${u.hostname.replace(/^www\./,'')}`:`${u.hostname.replace(/^www\./,'')} · bağlantı alındı`;}}catch{if(resolvingUrl===u.href){renderQualities([],'Video bilgisi alınamadı. Violet sunucusunun ve internet bağlantısının çalıştığını kontrol et.');$('#previewTitle').textContent=fallbackVideoTitle(u);$('#previewHost').textContent=u.hostname.replace(/^www\./,'');$('#previewProvider').textContent='KALİTE BİLGİSİ ALINAMADI';}}},250);
}
function showProModal() {
  showModal(`<span class="section-kicker">VIOLET PRO</span><h2>Kaliteni yükselt.</h2><p>İhtiyacına uygun süreyi seç. Devam ettiğinde sipariş hesabına kaydedilir; ödeme entegrasyonu henüz bağlı değildir.</p><div class="plan-choices"><label class="plan-choice"><input type="radio" name="duration" value="1" checked><span><b>1 Aylık</b><small>Esnek başlangıç</small></span><strong>₺99</strong></label><label class="plan-choice"><input type="radio" name="duration" value="2"><span><b>2 Aylık</b><small>₺89,50 / ay</small></span><strong>₺179</strong></label><label class="plan-choice"><input type="radio" name="duration" value="3"><span><b>3 Aylık</b><small>₺83 / ay · En avantajlı</small></span><strong>₺249</strong></label></div><p class="modal-note">Ödeme bağlantısı kurulana kadar siparişler SQL'de “ödeme bekliyor” olarak saklanır.</p><div class="modal-buttons"><button class="modal-secondary" id="redeemKey">Pro anahtarım var</button><button class="modal-primary" id="buyDemo">Sipariş oluştur <span>→</span></button></div>`);
  $('#redeemKey').onclick = keyModal;
  $('#buyDemo').onclick = async () => { const months = Number($('input[name="duration"]:checked').value); try { const r = await fetch('/api/orders', { method:'POST', credentials:'same-origin', headers:{'Content-Type':'application/json'}, body:JSON.stringify({months}) }); const data = await r.json(); if (!r.ok) throw new Error(data.error || 'Sipariş oluşturulamadı.'); closeModal(); toast(`₺${data.amount} tutarında sipariş kaydedildi · Ödeme bekliyor.`); } catch (e) { if (e.message.includes('giriş')) accountModal(); else toast(e.message.includes('fetch') ? 'SQL bağlantısı yok; SQLEXPRESS servisini başlat.' : e.message); } };
}
function keyModal() {
  showModal(`<span class="section-kicker">LİSANS AKTİVASYONU</span><h2>Pro anahtarını gir.</h2><p>Bu kodu hesabına tanımlayacağız.</p><div class="modal-form"><input id="keyEntry" placeholder="VIOLET-XXXX-XXXX-XXXX"><button class="modal-primary" id="activateKey">Devam et</button></div>`);
  $('#activateKey').onclick = async () => {
    const key=$('#keyEntry').value.trim().toUpperCase();if(!key)return toast('Aktivasyon kodunu gir.');
    try{const r=await fetch('/api/account/session',{credentials:'same-origin'});const data=await r.json();if(!data.authenticated){state.pendingLicense=key;activationAccountPrompt();return;}await redeemLicense(key);}
    catch(e){toast(e.message.includes('fetch')?'SQL bağlantısını kontrol et.':e.message);}
  };
}
function activationAccountPrompt(){showModal(`<span class="section-kicker">LİSANS AKTİVASYONU</span><h2>Kodu hesabına bağla.</h2><p>Pro lisansını hesabına kaydedebilmemiz için önce giriş yap veya yeni hesap oluştur. Kodun bu adımda korunacak.</p><div class="modal-buttons"><button id="licenseLogin" class="modal-primary">Giriş yap</button><button id="licenseRegister" class="modal-secondary">Hesap oluştur</button></div>`);$('#licenseLogin').onclick=()=>renderAccountForm('login');$('#licenseRegister').onclick=()=>renderAccountForm('register');}
async function redeemLicense(code){try{const r=await fetch('/api/license/redeem',{method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/json'},body:JSON.stringify({code})});const data=await r.json();if(!r.ok)throw new Error(data.error||'Kod doğrulanamadı.');state.pro=true;state.pendingLicense='';updatePlan();closeModal();toast(`${data.months} aylık Pro hesabına eklendi.`);return true;}catch(e){state.pendingLicense='';toast(e.message.includes('fetch')?'SQL bağlantısını kontrol et.':e.message);return false;}}
async function accountModal() {
  let account = null;
  try { const r = await fetch('/api/account/session', {credentials:'same-origin'}); if (r.ok) { const d = await r.json(); if (d.authenticated) account = d.user; } } catch {}
  if (account) { renderAccountModal(account); return; }
  renderAccountForm('login');
}
function renderAccountForm(mode) {
  const registering = mode === 'register';
  showModal(`<span class="section-kicker">VIOLET HESABI</span><h2>${registering ? 'Hesabını oluştur.' : 'Tekrar hoş geldin.'}</h2><p>Hesabın, Pro süren ve siparişlerin bu bilgisayardaki SQL Server veritabanında saklanır.</p>${registering ? '<label class="admin-login-label">Adın<input id="accountName" class="modal-input" autocomplete="name" placeholder="Ad soyad"></label>' : ''}<label class="admin-login-label">E-posta<input id="accountEmail" class="modal-input" type="email" autocomplete="email" placeholder="ornek@mail.com"></label><label class="admin-login-label">Şifre<input id="accountPassword" class="modal-input" type="password" autocomplete="${registering?'new-password':'current-password'}" placeholder="${registering?'En az 10 karakter':'Şifren'}"></label><div id="accountError" class="admin-error"></div><div class="modal-buttons"><button id="accountSubmit" class="modal-primary">${registering?'Hesap oluştur':'Giriş yap'}</button><button id="accountSwitch" class="modal-secondary">${registering?'Giriş yap':'Yeni hesap'}</button></div>`);
  $('#accountSubmit').onclick = async () => {
    const button = $('#accountSubmit'); button.disabled = true;
    try {
      const payload = registering ? {displayName:$('#accountName').value,email:$('#accountEmail').value,password:$('#accountPassword').value} : {username:$('#accountEmail').value,password:$('#accountPassword').value};
      const r = await fetch(`/api/account/${registering?'register':'login'}`,{method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)}); const data = await r.json(); if (!r.ok) throw new Error(data.error || 'İşlem tamamlanamadı.'); state.pro = state.isAdmin || !!data.user?.isPro; updatePlan(); if(state.pendingLicense){const code=state.pendingLicense;const activated=await redeemLicense(code);if(!activated)renderAccountModal(data.user);}else{renderAccountModal(data.user);toast(registering?'Hesap SQL Server\'a kaydedildi.':'Giriş yapıldı.');}
    } catch (e) { $('#accountError').textContent = e.message.includes('fetch') ? 'SQL bağlantısı yok. SQLEXPRESS servisini çalıştır.' : e.message; button.disabled = false; }
  };
  $('#accountSwitch').onclick = () => renderAccountForm(registering?'login':'register');
  $('#accountPassword').addEventListener('keydown', e => {if(e.key==='Enter') $('#accountSubmit').click();});
}
async function renderAccountModal(account) {
  let orders=[]; try { const r=await fetch('/api/orders',{credentials:'same-origin'});if(r.ok)orders=(await r.json()).orders||[]; } catch {}
  const orderRows=orders.length?orders.map(o=>`<div class="account-order"><span>${o.durationMonths} aylık Pro</span><b>₺${o.amount}</b><small>${o.status==='PendingPayment'?'Ödeme bekliyor':escapeHtml(o.status)} · ${new Date(o.createdAt).toLocaleDateString('tr-TR')}</small></div>`).join(''):'<div class="admin-output">Henüz sipariş yok.</div>';
  showModal(`<span class="section-kicker">VIOLET HESABI</span><h2>Merhaba, ${escapeHtml(account.displayName)}.</h2><p>${escapeHtml(account.email)}</p><div class="account-pro-status">${account.isPro?`Pro üyeliğin ${new Date(account.proExpiresAt).toLocaleDateString('tr-TR')} tarihine kadar açık.`:'Standart plan · 720p ve altı'}</div><div class="account-orders"><b>Sipariş geçmişi</b>${orderRows}</div><div class="modal-buttons"><button id="accountLogout" class="modal-secondary">Çıkış yap</button><button id="accountDone" class="modal-primary">Tamam</button></div>`);
  $('#accountDone').onclick=closeModal; $('#accountLogout').onclick=async()=>{await fetch('/api/account/logout',{method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/json'},body:'{}'});state.pro=state.isAdmin;updatePlan();closeModal();toast('Hesaptan çıkış yapıldı.');};
}
function adminModal() {
  if(state.isAdmin){adminDashboard();return;}
  showModal(`<span class="section-kicker">YÖNETİCİ PANELİ</span><h2>Admin girişi</h2><p>Hesapları, siparişleri ve Pro lisanslarını yönet.</p><label class="admin-login-label">Kullanıcı adı<input id="adminUsername" class="modal-input" autocomplete="username" value="Apoaxp"></label><label class="admin-login-label">Parola<input id="adminPassword" class="modal-input" type="password" autocomplete="current-password" placeholder="Yönetici parolası"></label><div id="adminLoginError" class="admin-error"></div><div class="modal-buttons"><button id="adminSignIn" class="modal-primary">Güvenli giriş</button><button id="adminCancel" class="modal-secondary">Vazgeç</button></div><p class="modal-note">Hesap doğrulaması yerel admin ayar dosyasıyla yapılır.</p>`);
  $('#adminCancel').onclick = closeModal;
  $('#adminSignIn').onclick = async () => {
    const button = $('#adminSignIn'); button.disabled = true; button.textContent = 'Kontrol ediliyor…';
    try { const r = await fetch('/api/admin/login', { method:'POST', credentials:'same-origin', headers:{ 'Content-Type':'application/json' }, body:JSON.stringify({ username:$('#adminUsername').value, password:$('#adminPassword').value }) }); const data = await r.json(); if (!r.ok) throw new Error(data.error || 'Giriş başarısız.'); state.isAdmin=true;state.pro=true;updatePlan();await adminDashboard(); }
    catch (e) { $('#adminLoginError').textContent = e.message.includes('fetch') ? 'Sunucu çalışmıyor. run.bat dosyasından Violet sunucusunu başlat.' : e.message; button.disabled = false; button.textContent = 'Güvenli giriş'; }
  };
  $('#adminPassword').addEventListener('keydown', e => { if (e.key === 'Enter') $('#adminSignIn').click(); });
}
async function adminDashboard() {
  try {
    const r = await fetch('/api/admin/keys', { credentials:'same-origin' }); const data = await r.json(); if (!r.ok) throw new Error(data.error || 'Oturum sona erdi.');
    showModal(`<span class="section-kicker">YÖNETİCİ PANELİ · OTURUM AÇIK</span><h2>Violet yönetimi</h2><div class="admin-tabs"><button class="selected" data-admin-tab="keys">Lisanslar</button><button data-admin-tab="orders">Siparişler</button><button data-admin-tab="users">Hesaplar</button></div><div id="adminPanel"><p>1, 2 veya 3 aylık Pro lisans anahtarı üret. Bilgiler SQL Server'da saklanır.</p><div class="modal-admin-grid"><label>Süre<select id="adminDuration"><option value="1">1 ay · ₺99</option><option value="2">2 ay · ₺179</option><option value="3">3 ay · ₺249</option></select></label><label>Adet<input id="adminQuantity" type="number" min="1" max="10" value="1"></label></div><div class="modal-buttons"><button id="generateKey" class="modal-primary">Anahtar üret</button><button id="adminLogout" class="modal-secondary">Çıkış yap</button></div><div id="adminResult">${adminKeysHtml(data.keys)}</div></div><p class="modal-note">Siparişler ödeme sağlayıcısı bağlanana kadar beklemede görünür.</p>`);
    $$('.admin-tabs button').forEach(tab => tab.onclick = () => { $$('.admin-tabs button').forEach(t => t.classList.toggle('selected', t === tab)); if (tab.dataset.adminTab === 'keys') adminDashboard(); else adminList(tab.dataset.adminTab); });
    $('#generateKey').onclick = async () => { const button = $('#generateKey'); button.disabled = true; try { const response = await fetch('/api/admin/keys', { method:'POST', credentials:'same-origin', headers:{ 'Content-Type':'application/json' }, body:JSON.stringify({ months:Number($('#adminDuration').value), quantity:Number($('#adminQuantity').value) }) }); const result = await response.json(); if (!response.ok) throw new Error(result.error || 'Anahtar üretilemedi.'); $('#adminResult').innerHTML = adminKeysHtml(result.keys, true) + adminKeysHtml(data.keys); toast('Lisans anahtarı SQL Server\'a kaydedildi.'); } catch (e) { toast(e.message); } finally { button.disabled = false; } };
    $('#adminLogout').onclick = async () => { await fetch('/api/admin/logout', { method:'POST', credentials:'same-origin', headers:{ 'Content-Type':'application/json' }, body:'{}' }); state.isAdmin=false;await syncEntitlements();adminModal(); };
  } catch (e) { if (e.message.includes('fetch')) toast('Violet sunucusuna ulaşılamıyor.'); else adminModal(); }
}
async function adminList(kind) {
  const target=$('#adminPanel'); if(!target)return; target.innerHTML='<div class="admin-output">Yükleniyor…</div>';
  try { const response=await fetch(`/api/admin/${kind}`,{credentials:'same-origin'});const data=await response.json();if(!response.ok)throw new Error(data.error||'Liste alınamadı.');
    if(kind==='orders') target.innerHTML=`<h3 class="admin-list-title">Siparişler (${data.orders.length})</h3>${data.orders.length?data.orders.map(o=>`<div class="admin-row"><b>${escapeHtml(o.email)}</b><span>${o.durationMonths} ay · ₺${o.amount}</span><small>${o.status==='PendingPayment'?'Ödeme bekliyor':escapeHtml(o.status)} · ${new Date(o.createdAt).toLocaleDateString('tr-TR')}</small></div>`).join(''):'<div class="admin-output">Henüz sipariş yok.</div>'}`;
    else target.innerHTML=`<h3 class="admin-list-title">Hesaplar (${data.users.length})</h3>${data.users.length?data.users.map(u=>`<div class="admin-row"><b>${escapeHtml(u.displayName)}</b><span>${escapeHtml(u.email)}</span><small>${u.orders} sipariş · kayıt ${new Date(u.createdAt).toLocaleDateString('tr-TR')}</small></div>`).join(''):'<div class="admin-output">Henüz hesap yok.</div>'}`;
  } catch(e) { target.innerHTML=`<div class="admin-error">${escapeHtml(e.message)}</div>`; }
}
function adminKeysHtml(keys, fresh = false) {
  if (!keys?.length) return '<div class="admin-output">Henüz lisans anahtarı yok.</div>';
  return keys.map(k => `<div class="key-result${fresh ? ' fresh-key' : ''}">${escapeHtml(k.code)} · ${k.months} ay <small>${new Date(k.createdAt).toLocaleDateString('tr-TR')}</small></div>`).join('');
}
function rewardsModal() {
  showModal(`<span class="section-kicker">ÖDÜLLÜ REKLAM · DEMO</span><h2>İzle, Pro hakkı kazan.</h2><p>Gerçek reklam gösterimi ve ödül onayı için Adsterra (veya uygun bir ödüllü reklam sağlayıcısı) entegrasyonu gerekir. Bu ekranda sayaç yalnızca arayüz demosudur.</p><div class="reward-card"><div class="reward-count"><span id="rewardNow">${state.reward}</span> / 4</div><small>reklam görüntülemesi · demo sayacı</small><div class="reward-progress"><i id="rewardBar" style="width:${state.reward*25}%"></i></div></div><div class="reward-demo">Bu buton gerçek reklam açmaz ve gerçek indirme hakkı oluşturmaz. Entegrasyon olmadan sayaç ilerletilmez.</div><div class="modal-buttons"><button id="closeReward" class="modal-secondary">Kapat</button><button id="rewardUnavailable" class="modal-primary">Reklam sağlayıcısını bağla</button></div>`);
  $('#closeReward').onclick = closeModal; $('#rewardUnavailable').onclick = () => toast('Reklam sağlayıcısı henüz entegre edilmedi.');
}
function renderLibrary() { $('#libraryCount').textContent = state.downloads.length; $('#libraryEmpty').classList.toggle('hidden', state.downloads.length > 0); $('#libraryItems').innerHTML = state.downloads.map(d => `<div class="library-row"><div class="preview-thumb">${d.mode === 'audio' ? '♫' : '▶'}</div><div><b>${escapeHtml(d.title)}</b><small>${escapeHtml(d.host)} · ${escapeHtml(d.quality)}</small></div><span class="standard-badge">İNDİRİLDİ</span></div>`).join(''); }
function escapeHtml(s) { return s.replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); }
function navigate(tab) { state.library = tab === 'library'; $('#studioView').classList.toggle('active-view', !state.library); $('#libraryView').classList.toggle('active-view', state.library); $$('.nav-item').forEach(n => n.classList.toggle('active', n.dataset.tab === tab)); $('#breadcrumbPage').textContent = state.library ? 'Kütüphanem' : 'İndirici'; }

$('#urlInput').addEventListener('input', inspectLink); $('#urlInput').addEventListener('paste', () => setTimeout(inspectLink, 0));
$('#pasteButton').onclick = async () => { try { $('#urlInput').value = await navigator.clipboard.readText(); inspectLink(); } catch { $('#urlInput').focus(); toast('Link alanına dokunup yapıştırabilirsin.'); } };
$('#clearLink').onclick = () => { $('#urlInput').value = ''; inspectLink(); };
$('#videoMode').onclick = () => { state.mode = 'video'; $('#videoMode').classList.add('selected'); $('#audioMode').classList.remove('selected'); $('#qualityOptions').classList.remove('hidden'); $('#audioOptions').classList.add('hidden'); };
$('#audioMode').onclick = () => { state.mode = 'audio'; $('#audioMode').classList.add('selected'); $('#videoMode').classList.remove('selected'); $('#qualityOptions').classList.add('hidden'); $('#audioOptions').classList.remove('hidden'); };
$('#qualityOptions').addEventListener('click', event => { const card=event.target.closest('.quality-card');if(!card)return;if(card.classList.contains('locked')&&!state.pro){showProModal();return;}setQuality(card.dataset.quality,card.dataset.height||({'4K':2160,'2K':1440,'1080p':1080,'720p':720,'480p':480,'360p':360}[card.dataset.quality])); });
$('#downloadButton').onclick = async () => { const u=parseLink($('#urlInput').value),msg=$('#statusMessage'),button=$('#downloadButton');msg.classList.remove('hidden');if(!u){msg.textContent='Önce geçerli bir video bağlantısı ekle.';return;}if(state.mode==='video'&&!state.availableQualities?.some(q=>Number(q.height)===state.height)){msg.textContent='Bu videonun kalite seçenekleri çözümlenmedi. Linki tekrar yapıştır.';return;}if(state.mode==='video'&&state.height>720&&!state.pro&&!state.isAdmin){showProModal();return;}button.disabled=true;msg.textContent='Video indiriliyor…';try{const r=await fetch('/api/video/download',{method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/json'},body:JSON.stringify({url:u.href,mode:state.mode,height:state.height})});const data=await r.json();if(!r.ok)throw new Error(data.error||'İndirme başarısız.');msg.textContent=`${data.message} · ${data.path}`;state.downloads.unshift({title:$('#previewTitle').textContent,host:u.hostname,quality:state.mode==='audio'?'MP3':state.quality,mode:state.mode});renderLibrary();toast('Video İndirilenler klasörüne kaydedildi.');}catch(e){msg.textContent=e.message;toast(e.message);}finally{button.disabled=false;} };
$('#upgradeButton').onclick = showProModal; $('#seePlans').onclick = showProModal; $('#adminButton').onclick = adminModal; $('#watchAdsButton').onclick = rewardsModal; $('#profileButton').onclick = accountModal;
$$('.nav-item').forEach(n => n.onclick = () => navigate(n.dataset.tab)); $('#backToStudio').onclick = () => navigate('studio'); $('#emptyStart').onclick = () => navigate('studio');
$('#modalClose').onclick = closeModal; $('.modal-backdrop').onclick = closeModal; document.addEventListener('keydown', e => { if (e.key === 'Escape') closeModal(); });
updatePlan(); renderLibrary();
async function syncEntitlements(){try{const [a,u]=await Promise.all([fetch('/api/admin/session',{credentials:'same-origin'}),fetch('/api/account/session',{credentials:'same-origin'})]);const admin=a.ok?await a.json():null;const account=u.ok?await u.json():null;state.isAdmin=!!admin?.authenticated;state.pro=state.isAdmin||!!account?.user?.isPro;updatePlan();}catch{}}
syncEntitlements();
fetch('/api/status').then(r=>r.ok?r.json():null).then(data=>{if(data&&!data.database)toast('SQL Server bağlantısı yok. SQLEXPRESS hizmetini başlatıp uygulamayı yenile.');}).catch(()=>{});
