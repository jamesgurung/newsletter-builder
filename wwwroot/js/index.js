// Common

const prevVals = [];
let inputsIdx = 0;
function rejectInvalidInput(elements) {
  for (const el of elements) {
    prevVals.push('');
    const idx = inputsIdx++;
    el.addEventListener('input', e => {
      e.target.value = e.target.value.toLowerCase();
      if (e.target.checkValidity()) prevVals[idx] = e.target.value;
      else e.target.value = prevVals[idx];
    });
  }
}

function bindEnterKey(elements) {
  for (const el of elements) {
    el.addEventListener('keypress', e => {
      if (e.keyCode === 13) {
        e.preventDefault();
        e.target.parentNode.getElementsByTagName('a')[0].click();
        return false;
      }
    });
  }
}

function clearAndFocus(ids) {
  for (const id of ids) {
    document.getElementById(id).value = '';
  }
  document.getElementById(ids[0]).focus();
}

function getOrder(ul) {
  const order = [];
  for (let i = 0; i < ul.children.length - 1; i++) {
    const name = ul.children[i].dataset.key.substr(11);
    if (name === 'intro') continue;
    order.push(name);
  }
  return order.join();
}

// Newsletters and Articles

document.querySelectorAll('#newsletters > li > ul').forEach(ul => {
  ul.appendChild(document.getElementById('addarticletemplate').content.cloneNode(true));
  if (ul.parentNode.dataset.enableadd !== 'true') ul.lastElementChild.style.display = 'none';
});
rejectInvalidInput(document.querySelectorAll('.articlename,.articlecontributors'));
bindEnterKey(document.querySelectorAll('.articlename,.articlecontributors'));

if (isEditor) {
  document.getElementById('addnewsletter').addEventListener('click', async () => {
    const date = document.getElementById('newsletterdate').value;
    const deadline = document.getElementById('newsletterdeadline').value;
    if (!date || !deadline) return;
    if (deadline > date) {
      alert('Deadline cannot be after publication date.');
      return;
    }
    const resp = await request('/api/newsletters', 'POST', { RowKey: date, Deadline: deadline });
    if (!resp.ok) return;
    const li = document.createElement('li');
    li.innerHTML = `<b>${date}</b> (deadline ${deadline}) <a class="publish" href="/${date}/publish">(Publish)</a> <a class="delete">&#10006;</a>`;
    li.dataset.key = date;
    const ul = document.createElement('ul');
    ul.appendChild(document.getElementById('addarticletemplate').content.cloneNode(true));
    li.appendChild(ul);
    document.getElementById('newsletters').insertBefore(li, document.getElementById('addnewslettersection'));
    rejectInvalidInput(ul.getElementsByTagName('input'));
    bindEnterKey(ul.getElementsByTagName('input'));

    const intro = document.createElement('li');
    const link = `${date}/intro`;
    intro.innerHTML = `<span class="status notstarted" title="Not Started"></span><a href="/${link}"><b>intro</b></a>`;
    intro.dataset.key = `${date}_intro`;
    ul.prepend(intro);

    let endDate = new Date(date);
    endDate.setDate(endDate.getDate() + 7);
    document.getElementById('newsletterdate').value = endDate.toISOString().slice(0, 10);
    endDate.setDate(endDate.getDate() - defaultDeadlineOffset);
    document.getElementById('newsletterdeadline').value = endDate.toISOString().slice(0, 10);
  });

  document.getElementById('newsletterdate').addEventListener('change', e => {
    if (!e.target.value) return;
    let date = new Date(e.target.value);
    date.setDate(date.getDate() - defaultDeadlineOffset);
    document.getElementById('newsletterdeadline').value = date.toISOString().slice(0, 10);
  });
}

document.getElementById('newsletters').addEventListener('click', async e => {
  if (e.target.classList.contains('delete')) {
    const li = e.target.closest('li');
    if (li.getElementsByTagName('ul')[0].getElementsByTagName('li').length > 2) {
      alert('Cannot delete newsletter which contains articles.');
      return;
    }
    const key = li.dataset.key;
    if (!confirm(`Are you sure you want to delete this newsletter?\n${key}`)) return;
    const resp = await request(`/api/newsletters/${key}`, 'DELETE');
    if (!resp.ok) return;
    li.remove();
  } else if (e.target.classList.contains('deletearticle')) {
    const li = e.target.closest('li');
    const key = li.dataset.key;
    if (!confirm(`Are you sure you want to delete this article?\n${key}`)) return;
    const order = getOrder(li.parentNode).split(',').filter(o => o !== key.substr(11)).join();
    const resp = await request(`/api/articles/${key}?order=${order}`, 'DELETE');
    if (!resp.ok) return;
    li.remove();
  } else if (e.target.classList.contains('addarticle')) {
    const inputs = e.target.parentNode.getElementsByTagName('input');
    const name = inputs[0].value;
    if (!name) return;
    if (name == 'publish') {
      alert('Article name cannot be "publish".');
      return;
    }
    let contributors;
    if (isEditor) {
      if (!inputs[1].value) return;
      contributors = inputs[1].value;
      for (const contributor of contributors.split(',')) {
        if (!usernames.includes(contributor)) {
          alert(`Contributor ${contributor} is not a valid username.`);
          return;
        }
      }
    } else {
      contributors = null;
    }
    const key = `${e.target.parentNode.parentNode.parentNode.dataset.key}_${name}`;
    const resp = await request('/api/articles', 'POST', { RowKey: key, Contributors: contributors });
    if (!resp.ok) return;
    const li = document.createElement('li');
    const link = key.replace('_', '/');
    const moveArticleButtons = isEditor ? '<a class="moveup">&#9650;</a> <a class="movedown">&#9660;</a>' : '';
    li.innerHTML = `<span class="status notstarted" title="Not Started"></span><a href="/${link}"><b>${name}</b></a> (${contributors ?? me}) ${moveArticleButtons} <a class="deletearticle">&#10006;</a>`;
    li.dataset.key = key;
    e.target.parentNode.parentNode.insertBefore(li, e.target.parentNode.parentNode.querySelector('.addarticlesection'));
    inputs[0].value = '';
    if (inputs.length >= 2) inputs[1].value = '';
    inputs[0].focus();
  } else if (e.target.classList.contains('moveup')) {
    const current = e.target.parentNode;
    const parent = current.parentNode;
    const siblings = parent.children;
    const index = Array.prototype.indexOf.call(siblings, current);
    if (index === 1) {
      const prevNewsletter = parent.parentNode.previousElementSibling;
      if (!prevNewsletter) return;
      if (!confirm('Are you sure you want to move this article to the previous newsletter?')) return;
      const prevNewsletterUl = prevNewsletter.getElementsByTagName('ul')[0];
      prevNewsletterUl.insertBefore(current, prevNewsletterUl.querySelector('.addarticlesection'));
      await request(`/api/articles/${current.dataset.key}/move`, 'POST', {
        Destination: prevNewsletter.dataset.key,
        SourceOrder: getOrder(parent),
        DestinationOrder: getOrder(prevNewsletterUl)
      });
      current.dataset.key = current.dataset.key.replace(parent.parentNode.dataset.key, prevNewsletter.dataset.key);
      current.querySelector('a').href = '/' + current.dataset.key.replace('_', '/');
    } else {
      parent.insertBefore(current, siblings[index - 1]);
      await request(`/api/newsletters/${parent.parentNode.dataset.key}/order`, 'PUT', { Order: getOrder(parent) });
    }
  } else if (e.target.classList.contains('movedown')) {
    const current = e.target.parentNode;
    const parent = current.parentNode;
    const siblings = parent.children;
    const index = Array.prototype.indexOf.call(siblings, current);
    if (index === siblings.length - 2) {
      const nextNewsletter = parent.parentNode.nextElementSibling;
      if (nextNewsletter.classList.contains('addnewslettersection')) return;
      if (!confirm('Are you sure you want to move this article to the next newsletter?')) return;
      const nextNewsletterUl = nextNewsletter.getElementsByTagName('ul')[0];
      nextNewsletterUl.insertBefore(current, nextNewsletterUl.children[1]);
      await request(`/api/articles/${current.dataset.key}/move`, 'POST', {
        Destination: nextNewsletter.dataset.key,
        SourceOrder: getOrder(parent),
        DestinationOrder: getOrder(nextNewsletterUl)
      });
      current.dataset.key = current.dataset.key.replace(parent.parentNode.dataset.key, nextNewsletter.dataset.key);
      current.querySelector('a').href = '/' + current.dataset.key.replace('_', '/');
    } else {
      parent.insertBefore(current, siblings[index + 2]);
      await request(`/api/newsletters/${parent.parentNode.dataset.key}/order`, 'PUT', { Order: getOrder(parent) });
    }
  }
});

// Events

bindEnterKey([document.getElementById('eventtitle')]);

document.getElementById('eventstart').addEventListener('change', e => {
  const start = e.target;
  const end = document.getElementById('eventend');
  if (!end.value || end.value < start.value) end.value = start.value;
  document.getElementById('eventtitle').focus();
});

document.getElementById('eventend').addEventListener('change', e => {
  const start = document.getElementById('eventstart');
  const end = e.target;
  if (!start.value || end.value < start.value) start.value = end.value;
  document.getElementById('eventtitle').focus();
});

document.getElementById('addevent').addEventListener('click', async e => {
  const title = document.getElementById('eventtitle').value.trim();
  const start = document.getElementById('eventstart').value;
  let end = document.getElementById('eventend').value;
  if (!title || !start) return;
  const resp = await request('/api/events', 'POST', { RowKey: `${start}_${end}_${title}` });
  if (!resp.ok) return;
  const ev = await resp.json();
  const li = document.createElement('li');
  li.innerHTML = `<b>${ev.displayDate}</b> - ${ev.displayTitle}${isEditor ? '' : ' (Pending Approval)'} <a class="delete">&#10006;</a>`;
  li.dataset.key = ev.rowKey;
  if (!isEditor) li.classList.add('pendingapproval');
  document.getElementById('events').insertBefore(li, document.getElementById('addeventsection'));
  clearAndFocus(['eventstart', 'eventend', 'eventtitle']);
});

document.getElementById('events').addEventListener('click', async e => {
  if (e.target.classList.contains('delete')) {
    const li = e.target.closest('li');
    if (!confirm(`Are you sure you want to delete this event?\n${li.innerText.slice(0, -4)}`)) return;
    const key = li.dataset.key;
    const resp = await request(`/api/events/${key}`, 'DELETE');
    if (!resp.ok) return;
    li.remove();
  } else if (e.target.classList.contains('approve')) {
    const li = e.target.closest('li');
    const key = li.dataset.key;
    const resp = await request(`/api/events/${key}/approve`, 'POST');
    if (!resp.ok) return;
    li.classList.remove('pendingapproval');
    e.target.remove();
  }
});

// Users
if (isEditor) {
  rejectInvalidInput([document.getElementById('username')]);
  bindEnterKey([document.getElementById('userfirst')]);

  document.getElementById('adduser').addEventListener('click', async e => {
    const username = document.getElementById('username').value;
    const userfirst = document.getElementById('userfirst').value;
    const userdisplay = document.getElementById('userdisplay').value;
    if (!username || !userfirst || !userdisplay) return;
    const resp = await request('/api/users', 'POST', { RowKey: username, FirstName: userfirst, DisplayName: userdisplay });
    if (!resp.ok) return;
    const li = document.createElement('li');
    li.innerHTML = `<b>${username}</b> - ${userdisplay} (${userfirst}) <a class="delete">&#10006;</a>`;
    li.dataset.key = username;
    document.getElementById('users').insertBefore(li, document.getElementById('addusersection'));
    clearAndFocus(['username', 'userdisplay', 'userfirst']);
    usernames.push(username);
  });

  document.getElementById('users').addEventListener('click', async e => {
    if (!e.target.classList.contains('delete')) return;
    const li = e.target.closest('li');
    const key = li.dataset.key;
    if (!confirm(`Are you sure you want to delete this user?\n${key}`)) return;
    const resp = await request(`/api/users/${key}`, 'DELETE');
    if (!resp.ok) return;
    li.remove();
    usernames.splice(usernames.indexOf(key), 1);
  });
}