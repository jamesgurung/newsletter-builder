// Globals

const article = document.getElementsByTagName('article')[0];
const headline = document.getElementById('headline');
const prevVals = [];
let inputsIdx = 0;
let saved = true;
let typing = false;

// Load article

if (content) {
  headline.innerText = content.headline;
  for (const section of content.sections) {
    addSection(section.includeImage, section.text, section.image, section.alt, section.consent, section.consentNotes);
  }
} else {
  if (articleKey.substring(11) === 'intro') {
    addSection(false);
  } else {
    addSection(true); addSection(true); addSection(true);
  }
}
configureInput([document.getElementById('headline')]);
if (isSubmitted) {
  lockEditing();
} else {
  document.getElementById('submit').style.display = 'none';
  if (isEditor) document.getElementById('approve').style.display = 'none';
}
document.getElementById('main').style.display = '';
updateCoverPhotoLinks();

// Add section

function addSection(includeImage, text, image, alt, consent, consentNotes) {
  const section = document.getElementById('sectiontemplate').content.cloneNode(true)
  article.appendChild(section);
  article.lastElementChild.dataset.includeimage = includeImage ? 'true' : 'false';
  const textElement = article.lastElementChild.getElementsByClassName('section-text')[0];
  configureInput([textElement]);
  if (text) textElement.innerText = text;
  const imageElement = article.lastElementChild.getElementsByClassName('section-image')[0];
  if (includeImage) {
    imageElement.getElementsByTagName('input')[0].addEventListener('change', onSelectImage);
    const checkbox = imageElement.querySelector('input[type="checkbox"]');
    checkbox.addEventListener('change', async e => {
      e.target.closest('.section-image').getElementsByClassName('consent-notes')[0].style.display = e.target.checked ? 'none' : null;
      if (!await save()) return;
      flash(e.target.parentNode.parentNode);
    });
    configureInput([imageElement.querySelector('.alt-text'), imageElement.querySelector('.consent-notes')]);
    setImage(imageElement, image, alt, consent, consentNotes);
  } else {
    imageElement.remove();
  }
}

function setImage(el, image, alt, consent, consentNotes) {
  if (image) {
    [...el.children].forEach(c => c.style.display = null);
    el.dataset.image = image;
    el.getElementsByTagName('img')[0].src = `${blobBaseUrl}${domain}/${articleKey}/${image}?${sas}`;
    el.getElementsByTagName('label')[0].style.display = 'none';
    el.getElementsByClassName('set-cover-photo-section')[0].style.display = 'none';
    el.querySelector('input[type="checkbox"]').checked = consent;
    el.querySelector('.consent-notes').style.display = consent ? 'none' : null;
    if (alt) el.querySelector('.alt-text').innerText = alt;
    if (consentNotes) el.querySelector('.consent-notes').innerText = consentNotes;
  } else {
    [...el.children].forEach(c => c.style.display = 'none');
    el.dataset.image = '';
    el.getElementsByTagName('label')[0].style.display = null;
    el.querySelector('.alt-text').innerText = '';
    el.querySelector('input[type="checkbox"]').checked = false;
    el.querySelector('.consent-notes').innerText = '';
  }
}

// Configure input constraints and autosave

async function save() {
  content = {
    headline: headline.innerText,
    sections: [...article.getElementsByTagName('section')].map(el => {
      const imageSection = el.getElementsByClassName('section-image')[0];
      const includeImage = el.dataset.includeimage === 'true';
      const imageUrl = includeImage && imageSection.dataset.image ? imageSection.dataset.image : null;
      const altText = imageUrl ? imageSection.querySelector('.alt-text').innerText : null;
      const consent = imageUrl ? imageSection.querySelector('input[type="checkbox"]').checked : false;
      const consentNotes = (!imageUrl || consent) ? null : imageSection.querySelector('.consent-notes').innerText;
      return {
        text: el.getElementsByClassName('section-text')[0].innerText,
        includeImage: includeImage,
        image: imageUrl,
        alt: altText ? altText : null,
        consent: consent,
        consentNotes: consentNotes ? consentNotes : null
      };
    })
  };
  const resp = await request(`/api/articles/${articleKey}/content`, 'PUT', content);
  if (resp.ok) document.getElementById('requestaifeedback').classList.remove('disabled');
  return resp.ok;
}

function configureInput(elements) {
  for (const el of elements) {
    el.addEventListener('input', e => {
      let pos = document.getSelection().focusOffset;
      let text = e.target.innerText;
      if (/\n/g.test(text)) {
        pos = text.indexOf('\n');
        text = text.replace(/[\r\n]+/g, '')
      }
      const originalLength = text.length;
      const maxLength = e.target.dataset.maxlength;
      text = text.replace(/[\u2018\u2019]/g, "'").replace(/[\u201C\u201D]/g, '"').slice(0, maxLength).replace(/[^A-Za-z0-9\[\]\*!"£$%&();:',.=_#@\/?\s\u00E0\u00E2\u00E6\u00E7\u00E9\u00E8\u00EA\u00EB\u00EE\u00EF\u00F4\u0153\u00F9\u00FB\u00FC\u00FF\u00C0\u00C2\u00C6\u00C7\u00C9\u00C8\u00CA\u00CB\u00CE\u00CF\u00D4\u0152\u00D9\u00DB\u00DC\u0178\u00A1\u00BF\+-]+/, '');
      if (text !== e.target.innerText) {
        e.target.innerText = text;
        pos = pos - (originalLength - e.target.innerText.length);
        if (pos > 0) document.getSelection().setPosition(e.target.firstChild, pos);
      }
      saved = false;
    });

    el.addEventListener('blur', async e => {
      if (saved) return;
      e.target.innerText = e.target.innerText.replace(/[\f\t\v\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff]/g, ' ').replace(/\s\s+/g, ' ').trim();
      if (!await save()) return;
      flash(e.target);
      saved = true;
    });
  }
}

function flash(el) {
  el.style.transition = '';
  el.style.backgroundColor = '#ddd';
  window.setTimeout(() => { el.style.transition = 'background-color 0.5s'; el.style.backgroundColor = 'unset'; }, 500);
}

window.addEventListener('beforeunload', e => {
  if (!saved) e.returnValue = 'warn';
});

// Add and remove sections

document.getElementById('addsection').addEventListener('click', () => { addSection(true); });
if (isEditor || isOwner) {
  document.getElementById('addtextsection').addEventListener('click', () => { addSection(false); });
}

article.addEventListener('click', async e => {
  if (e.target.classList.contains('section-delete')) {
    const section = e.target.closest('section');
    if (section.querySelector('.section-text').innerText || (section.dataset.includeimage === 'true' && section.querySelector('.section-image').dataset.image)) {
      if (!confirm('Are you sure you want to delete this paragraph and photo?')) return;
    }
    if (section.dataset.includeimage === 'true') {
      var imageName = section.querySelector('.section-image').dataset.image;
      if (imageName) {
        await request(`/api/articles/${articleKey}/image/${imageName}`, 'DELETE');
      }
    }
    section.remove();
    await save();
  }
  else if (e.target.classList.contains('clear-image')) {
    if (!confirm('Are you sure you want to delete this photo?')) return;
    const imageSection = e.target.closest('.section-image');
    const imageName = imageSection.dataset.image;
    setImage(imageSection, null);
    await request(`/api/articles/${articleKey}/image/${imageName}`, 'DELETE');
    await save();
  }
  else if (e.target.classList.contains('set-cover-photo')) {
    const imageSection = e.target.closest('.section-image');
    coverPhoto = imageSection.dataset.image;
    await request(`/api/newsletters/${articleKey.slice(0, 10)}/coverphoto`, 'PUT', { ArticleKey: articleKey, ImageName: coverPhoto });
    updateCoverPhotoLinks();
  }
});

// Submit article

function isValid(verb) {
  if (!content) { alert('Unable to ' + verb + ': Article is blank.'); return false; }
  if (!content.headline) { alert('Unable to ' + verb + ': Headline required.'); return false; }
  if (!content.sections.length) { alert('Unable to ' + verb + ': Article must have at least one paragraph.'); return false; }
  if (content.sections.some(s => !s.text)) { alert('Unable to ' + verb + ': Article cannot contain empty paragraphs.'); return false; }
  if (content.sections.some(s => s.text.length < 40)) { alert('Unable to ' + verb + ': Paragraphs must be at least 40 characters.'); return false; }
  if (content.sections.some(s => s.text.length > 500)) { alert('Unable to ' + verb + ': Paragraphs must be no longer than 500 characters.'); return false; }
  if (content.sections.some(s => s.includeImage && !s.image)) { alert('Unable to ' + verb + ': All paragraphs must contain a photo.'); return false; }
  if (content.sections.some(s => s.includeImage && !s.alt)) { alert('Unable to ' + verb + ': All photos must include a description.'); return false; }
  if (content.sections.some(s => s.includeImage && !s.consent && !s.consentNotes)) { alert('Unable to ' + verb + ': All photos must be ticked for photo consent, or contain details about which students do not consent.'); return false; }
  for (const word of bannedWords) {
    if (content.sections.some(s => s.text.toLowerCase().includes(word))) { alert(`Unable to ' + verb + ': Avoid using the word "${word}".`); return false; }
  }
  return true;
}

document.getElementById('submit').addEventListener('click', async () => {
  if (isSubmitted) {
    if (!isEditor) return;
    if (!confirm('Are you sure you want to unsubmit this article?')) return;
    await request(`/api/articles/${articleKey}/unsubmit`, 'POST');
    const button = document.getElementById('submit');
    button.innerText = 'Submit completed article to editor';
    button.classList.remove('disabled');
    button.style.cursor = '';
    document.getElementById('approve').style.display = 'none';
    isSubmitted = false;
    return;
  }
  if (!isValid('submit')) return;
  if (!confirm('Are you sure you are ready to submit this article?\nNo changes can be made after submission.')) return;
  await request(`/api/articles/${articleKey}/submit`, 'POST');
  lockEditing();
  isSubmitted = true;
});

if (isEditor) {
  document.getElementById('approve').addEventListener('click', async () => {
    if (isApproved) {
      await request(`/api/articles/${articleKey}/unapprove`, 'POST');
      const button = document.getElementById('approve');
      button.innerText = 'Approve article';
      button.classList.remove('disabled');
      button.style.cursor = '';
      isApproved = false;
      [...document.querySelectorAll('#headline,.section-text,.alt-text,.consent-notes')].forEach(el => el.contentEditable = true);
      [...document.querySelectorAll('.section-delete,.clear-image,#addsectioncontainer,#submit')].forEach(el => el.style.display = '');
      [...document.querySelectorAll('.section-text,.section-image,#headline')].forEach(el => el.style.border = '');
      [...document.querySelectorAll('input')].forEach(el => el.disabled = false);
      updateCoverPhotoLinks();
      document.getElementById('requestaifeedback').style.display = '';
      return;
    }
    if (content.sections.some(s => s.includeImage && !s.consent)) { alert('Unable to approve: some students do not have photo consent.'); return; }
    await request(`/api/articles/${articleKey}/approve`, 'POST');
    isApproved = true;
    lockEditing();
  });
}

function lockEditing() {
  const button = document.getElementById('submit');
  const approveButton = document.getElementById('approve');
  const aiButton = document.getElementById('requestaifeedback');
  updateCoverPhotoLinks();
  if (isEditor) {
    button.innerText = 'Unsubmit';
    button.classList.add('disabled');
    button.style.cursor = 'pointer';
    approveButton.style.display = '';
  }
  if (!isEditor || isApproved) {
    if (isEditor) {
      button.style.display = 'none';
      approveButton.innerText = 'Unapprove';
      approveButton.classList.add('disabled');
      approveButton.style.cursor = 'pointer';
    } else {
      button.innerText = isApproved ? 'Approved by editor' : 'Submitted to editor';
    }
    aiButton.style.display = 'none';
    [...document.querySelectorAll('#headline,.section-text,.alt-text,.consent-notes')].forEach(el => el.contentEditable = false);
    [...document.querySelectorAll('.section-delete,.clear-image,#addsectioncontainer')].forEach(el => el.style.display = 'none');
    [...document.querySelectorAll('.section-text,.section-image,#headline')].forEach(el => el.style.border = 'none');
    [...document.querySelectorAll('input')].forEach(el => el.disabled = true);
    
    button.classList.add('disabled');
  }
}

// Image cropping and uploading

let cropper;
let currentImageInputElement;

function onSelectImage(e) {
  if (e.target.files.count === 0 || !e.target.files[0]) return;
  const type = e.target.files[0].type;
  if (type !== 'image/jpeg' && type !== 'image/png') {
    alert('Only JPG and PNG images are supported.');
    return;
  }
  const image = document.getElementById('cropper-image');
  image.onload = async () => {
    if (image.naturalWidth < 1120 || image.naturalHeight < 630) {
      alert('Please select a higher-quality photo.\nThe image must be at least 1120 x 630 pixels.');
      return;
    }
    currentImageInputElement = e.target;
    if (image.naturalWidth === 1120 && image.naturalHeight === 630) {
      await uploadImage(e.target.files[0], e.target.files[0].type);
      return;
    }
    document.getElementById('image-editor').style.display = 'block';
    image.addEventListener('ready', () => {
      document.getElementById('image-editor').style.opacity = 1;
    });
    cropper = new Cropper(image, {
      viewMode: 2,
      aspectRatio: 16 / 9,
      autoCropArea: 0.9,
      scalable: false,
      dragMode: 'move',
      toggleDragModeOnDblclick: false,
      crop: function (event) {
        var width = Math.round(event.detail.width);
        var height = Math.round(event.detail.height);

        if (width < 1120 || height < 630) {
          cropper.setData({
            width: Math.max(1120, width),
            height: Math.max(630, height),
          });
        }
      }
    });

  }
  image.src = URL.createObjectURL(e.target.files[0]);
}

document.getElementById('image-cancel').addEventListener('click', () => {
  const editor = document.getElementById('image-editor');
  editor.style.display = 'none';
  editor.style.opacity = 0;
  cropper.destroy();
  currentImageInputElement.value = null;
  currentImageInputElement = null;
});

document.getElementById('image-upload').addEventListener('click', async () => {
  const canvas = cropper.getCroppedCanvas({ width: 1120, height: 630 });
  const type = currentImageInputElement.files[0].type;
  const blob = await new Promise(resolve => canvas.toBlob(resolve, type, 0.9));
  const editor = document.getElementById('image-editor');
  editor.style.display = 'none';
  editor.style.opacity = 0;
  await uploadImage(blob, type);
  cropper.destroy();
});

document.getElementById('rotate-left').addEventListener('click', () => { cropper.clear(); cropper.rotate(-90); cropper.crop(); });
document.getElementById('rotate-right').addEventListener('click', () => { cropper.clear(); cropper.rotate(90); cropper.crop(); });

async function uploadImage(blob, type) {
  const resp = await fetch(`/api/articles/${articleKey}/image`, {
    method: 'POST',
    headers: { 'X-XSRF-TOKEN': antiforgeryToken, 'Content-Type': type },
    body: blob
  });
  if (!resp.ok) {
    alert(await resp.json());
    return;
  }
  currentImageInputElement.value = null;
  const imageUrl = await resp.json();
  const imageSection = currentImageInputElement.closest('.section-image');
  setImage(imageSection, imageUrl);
  if (!await save()) return;
}

// Cover photo

function updateCoverPhotoLinks() { 
  [...document.getElementsByClassName('set-cover-photo-section')].forEach(el => {
    if (!isEditor || !isApproved) {
      el.style.display = 'none';
      return;
    }
    el.style.display = '';
    const isCoverPhoto = el.parentNode.dataset.image === coverPhoto;
    el.getElementsByClassName('set-cover-photo')[0].style.display = isCoverPhoto ? 'none' : '';
    el.getElementsByClassName('cover-photo-set')[0].style.display = isCoverPhoto ? '' : 'none';
  });
}

// AI feedback

const connection = new signalR.HubConnectionBuilder().withUrl('/chat').withAutomaticReconnect().build();
connection.start();
document.addEventListener('visibilitychange', async () => {
  if (document.visibilityState === 'visible' && connection.state === signalR.HubConnectionState.Disconnected) await connection.start();
});

document.getElementById('requestaifeedback').addEventListener('click', async e => {
  if (e.target.classList.contains('disabled')) return;
  if (!isValid('request review')) return;
  e.target.classList.add('disabled');
  var div = document.getElementById('aifeedbackcontent');
  div.innerHTML = '';
  document.getElementById('aifeedbackbubble').style.display = '';
  document.getElementById('aifeedback').style.display = '';
  document.getElementById('aifeedbackfinish').style.display = 'none';
  window.scrollTo(0, document.body.scrollHeight);
  typing = true;
  var resp = await request(`/api/aifeedback`, 'POST', {
    Identifier: articleKey,
    Headline: content.headline,
    Content: content.sections.map(s => s.text + '\n' + (s.includeImage ? (s.altText ? `[Photo - Caption: ${s.altText}]` : '[Photo]') : '')).join('\n'),
    ConnectionId: connection.connectionId
  });
  typing = false;
  var bullets = await resp.json();
  var html = bullets.split('\n')
    .filter(o => o)
    .map(o => o.slice(2).trim())
    .map(o => `<li>${o}</li>`)
    .join('');
  div.innerHTML = '<ul>' + html + '</ul>';
  document.getElementById('aifeedbackbubble').style.display = 'none';
  document.getElementById('aifeedbackfinish').style.display = '';
  document.getElementById('submit').style.display = '';
});

const chat = document.getElementById('aifeedbackcontent');
connection.on('Type', function (token) {
  if (!typing) return;
  if (chat.innerHTML.length === 0) chat.innerHTML = '<ul></ul>';
  const ul = chat.getElementsByTagName('ul')[0];
  if (token === '*') ul.appendChild(document.createElement("li"));
  else ul.querySelector('li:last-child').textContent += token.replace(/\n/g, ' ');
});