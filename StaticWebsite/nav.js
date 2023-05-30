const logo = document.getElementsByClassName('logo')[0];
logo.style.margin = '0';
logo.outerHTML = "<a style=\"display: inline-block; width: 125px; margin: 0 auto\" href=\"/\">" + logo.outerHTML + "</a>";

const header = document.getElementById('webheader');

fetch('/list.json').then(res => res.json()).then(list => {
  const today = new Date().toISOString().slice(0, 10);
  for (let i = 0; i < list.length; i++) {
    if (list[i].date === window.location.pathname.replace(/\//g, '')) {
      if (i < list.length - 1) {
        const prev = document.createElement('a');
        prev.href = '/' + list[i + 1].date;
        prev.textContent = '<< ' + formatDate(list[i + 1].date);
        prev.style.marginRight = 'auto';
        prev.style.color = '#1188E6';
        prev.style.textDecoration = 'none';
        header.appendChild(prev);
      }
      if (i > 0 && list[i - 1].date <= today && (list[i - 1].date < today || new Date().getHours() >= 15)) {
        const next = document.createElement('a');
        next.href = '/' + list[i - 1].date;
        next.textContent = formatDate(list[i - 1].date) + ' >>';
        next.style.marginLeft = 'auto';
        next.style.color = '#1188E6';
        next.style.textDecoration = 'none';
        header.appendChild(next);
      }
      break;
    }
  }
  const header2 = header.cloneNode(true);
  header2.id = 'webheader2';
  header.parentElement.appendChild(header2);
});

function formatDate(isoDateString) {
  const date = new Date(isoDateString);
  return date.getDate() + ' ' + date.toLocaleString('en-GB', { month: 'long' }) + ' ' + date.getFullYear();
}