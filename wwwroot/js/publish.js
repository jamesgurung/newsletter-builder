const connection = new signalR.HubConnectionBuilder().configureLogging(signalR.LogLevel.Warning).withUrl('/chat').withAutomaticReconnect().build();
connection.start();
document.addEventListener('visibilitychange', async () => {
  if (document.visibilityState === 'visible' && connection.state === signalR.HubConnectionState.Disconnected) await connection.start();
});

document.getElementById('publish')?.addEventListener('click', async () => {
  const desc = prompt('To publish this newsletter, please enter a description.', description);
  if (!desc) return;
  document.getElementById('publish-box').textContent = 'Publishing...';
  const html = await fetch(`/${key}`).then(resp => resp.text());
  await request(`/api/newsletters/${key}/publish`, 'POST', { html: html, description: desc });
  window.location.reload();
});

[...document.getElementsByClassName('send')].forEach(o => o.addEventListener('click', sendEmails));

async function sendEmails(e) {
  const to = e.target.dataset.to;
  if (to === 'all') {
    if (!confirm('Are you sure you want to send this newsletter to the whole mailing list?')) return;
    document.getElementById('send-box').textContent = 'Sending...';
    document.getElementById('progress').style.display = 'block';
  }
  var resp = await request(`/api/newsletters/${key}/send`, 'POST', { to: to, connectionId: connection.connectionId });
  if (!resp.ok) {
    document.getElementById('send-box').innerHTML = '<span class="red">Sending failed.</span>';
    document.getElementById('progress').style.display = 'none';
    return;
  }
  if (to === 'preview') {
    alert('Preview message sent.');
  } else if (to === 'qa') {
    alert('QA message sent.');
  } else if (to === 'all') {
    document.getElementById('send-box').innerHTML = '<span class="green">Done</span>';
    document.getElementById('progress-bar').style.width = '100%';
  }
}

connection.on('SendProgress', function (perc) {
  document.getElementById('progress-bar').style.width = `${perc}%`;
});