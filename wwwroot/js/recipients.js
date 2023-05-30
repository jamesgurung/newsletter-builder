document.getElementById('upload').addEventListener('click', () => {
  document.getElementById('files').click();
});

document.getElementById('files').addEventListener('change', async (event) => {
  const files = event.target.files;
  let validEmails = [];

  for (const file of files) {
    try {
      const content = await readFileAsText(file);
      const lines = content.split('\n');
      const validLines = lines.filter(line => line.includes('@')).map(line => line.trim().toLowerCase());
      Array.prototype.push.apply(validEmails, validLines);
    } catch (error) {
      alert(`Failed to read ${file.name}`);
      return;
    }
  }

  validEmails = [...new Set(validEmails)];
  if (validEmails.length === 0) {
    alert('No valid emails found to upload.');
    return;
  }
  document.getElementById('status').innerHTML = '<i>Uploading...</i>';
  const resp = await request('/api/recipients', 'POST', { recipients: validEmails });
  if (!resp.ok) {
    document.getElementById('status').innerHTML = '<b>Upload failed.</b>';
    return;
  }
  document.getElementById('status').innerHTML = `<b>Success: ${validEmails.length} recipients uploaded.</b>`;
});

function readFileAsText(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();

    reader.onload = event => resolve(event.target.result);
    reader.onerror = error => reject(error);

    reader.readAsText(file);
  });
}