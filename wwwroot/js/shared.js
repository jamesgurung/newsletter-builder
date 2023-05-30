async function request(url, method, body) {
  const headers = { 'X-XSRF-TOKEN': antiforgeryToken };
  if (body) headers['Content-Type'] = 'application/json';
  const resp = await fetch(url, {
    method: method,
    headers: headers,
    body: body ? JSON.stringify(body) : null
  });
  if (!resp.ok) {
    const text = await resp.text();
    alert(text ? JSON.parse(text) : 'An error occurred.');
  }
  return resp;
}
