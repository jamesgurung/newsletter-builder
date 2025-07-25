// Globals

const article = document.getElementsByTagName('article')[0];
const headline = document.getElementById('headline');
const invalidCharacters = /[^A-Za-z0-9\[\]\*!"£$%&();:',.=_#@\/?\s\u00E0\u00E2\u00E6\u00E7\u00E9\u00E8\u00EA\u00EB\u00EE\u00EF\u00F4\u0153\u00F9\u00FB\u00FC\u00FF\u00C0\u00C2\u00C6\u00C7\u00C9\u00C8\u00CA\u00CB\u00CE\u00CF\u00D4\u0152\u00D9\u00DB\u00DC\u0178\u00A1\u00BF\+-]+/g;
const invalidWhitespace = /[\f\t\v\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff]/g;
const repeatedSpaces = /\s\s+/g;

let inputsIdx = 0;
let saved = true;

// Load article

if (content) {
  headline.innerText = content.headline;
  for (const section of content.sections) {
    addSection(section.includeImage, section.text, section.image, section.alt, section.consent);
  }
} else {
  if (isIntro) {
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

function addSection(includeImage, text, image, alt, consent) {
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
      if (!await save()) return;
      flash(e.target.parentNode.parentNode);
    });
    configureInput([imageElement.querySelector('.alt-text')]);
    setImage(imageElement, image, alt, consent);
  } else {
    imageElement.remove();
  }
}

function setImage(el, image, alt, consent) {
  const uploadElement = el.querySelector('label');
  const imageElement = el.querySelector('img');
  const altTextElement = el.querySelector('.alt-text');
  const consentAreaElement = el.querySelector('.consent');
  const consentElement = el.querySelector('input[type="checkbox"]');
  const clearElement = el.querySelector('.clear-image-section');
  const approveElement = el.querySelector('.approve-image-section');
  const setCoverPhotoElement = el.querySelector('.set-cover-photo-section');
  const blurImageElement = el.querySelector('.blur-image-section');
  const isGenerating = alt === 'generating';
  const isInvalid = alt === 'invalid';

  el.dataset.image = (image && image.substring(0, 5) !== 'blob:') ? image : '';

  uploadElement.style.display = image ? 'none' : null;
  imageElement.src = image ? (image.substring(0, 5) === 'blob:' ? image : `${blobBaseUrl}${domain}/${articleKey}/${image}?${sas}`) : '';
  imageElement.style.display = image ? null : 'none';
  altTextElement.contentEditable = !isGenerating && !isInvalid;
  altTextElement.className = isGenerating ? 'alt-text alt-text-loading' : (isInvalid ? 'alt-text alt-text-error' : 'alt-text');
  if (isGenerating) altTextElement.innerHTML = '<div class="typing" title="Generating caption..."><span></span><span></span><span></span></div>';
  else altTextElement.innerText = isInvalid ? 'Only photos of students, staff, or student work are accepted. Please upload a different photo.' : (alt ?? '');
  altTextElement.style.display = image ? null : 'none';
  consentAreaElement.style.display = image && !isGenerating && !isInvalid ? null : 'none';
  consentElement.checked = image ? consent : false;
  clearElement.style.display = image && !isGenerating ? null : 'none';
  blurImageElement.style.display = image && !isGenerating && !isInvalid ? null : 'none';
  approveElement.style.display = image && isInvalid && isEditor ? null : 'none';
  setCoverPhotoElement.style.display = 'none';
}

// Configure input constraints and autosave

async function save() {
  content = {
    headline: headline.innerText,
    sections: [...article.getElementsByTagName('section')].map(el => {
      const imageSection = el.getElementsByClassName('section-image')[0];
      const includeImage = el.dataset.includeimage === 'true';
      const imageUrl = includeImage && imageSection.dataset.image ? imageSection.dataset.image : null;
      const alt = imageUrl ? imageSection.querySelector('.alt-text') : null;
      const isInvalidImage = imageUrl ? alt.classList.contains('alt-text-error') : null;
      const isAltTextLoading = imageUrl ? alt.classList.contains('alt-text-loading') : null;
      const altText = imageUrl ? (isInvalidImage ? 'invalid' : (isAltTextLoading ? '' : alt.innerText)) : null;
      const consent = imageUrl ? imageSection.querySelector('input[type="checkbox"]').checked : false;
      return {
        text: el.getElementsByClassName('section-text')[0].innerText,
        includeImage: includeImage,
        image: imageUrl,
        alt: altText ? altText : null,
        consent: consent
      };
    }),
    etag
  };
  const resp = await request(`/api/articles/${articleKey}/content`, 'PUT', content);
  if (!resp.ok) {
    saved = true;
    window.location.reload();
    return false;
  }
  document.getElementById('requestaifeedback').classList.remove('disabled');
  etag = await resp.json();
  return true;
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
      text = text.replace(/[\u2018\u2019]/g, "'").replace(/[\u201C\u201D]/g, '"').slice(0, maxLength).replace(invalidCharacters, '');
      if (text !== e.target.innerText) {
        e.target.innerText = text;
        pos = pos - (originalLength - e.target.innerText.length);
        if (pos > 0) document.getSelection().setPosition(e.target.firstChild, pos);
      }
      saved = false;
    });

    el.addEventListener('blur', async e => {
      if (saved) return;
      e.target.innerText = e.target.innerText.replace(invalidWhitespace, ' ').replace(repeatedSpaces, ' ').trim();
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
      const imageName = section.querySelector('.section-image').dataset.image;
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
  else if (e.target.classList.contains('blur-start')) {
    const imageSection = e.target.closest('.section-image');
    const imageName = imageSection.dataset.image;
    openBlurPanel(imageSection, imageName);
  }
  else if (e.target.classList.contains('approve-image')) {
    const imageSection = e.target.closest('.section-image');
    const imageName = imageSection.dataset.image;
    setImage(imageSection, imageName);
    await save();
  }
  else if (e.target.classList.contains('set-cover-photo')) {
    coverPhoto = e.target.closest('.section-image').dataset.image;
    await request(`/api/newsletters/${articleKey.slice(0, 10)}/coverphoto`, 'PUT', { ArticleKey: articleKey, ImageName: coverPhoto });
    updateCoverPhotoLinks();
  }
});

// Submit article

function isValid(verb) {
  if (!content) { alert(`Unable to ${verb}: Article is blank.`); return false; }
  if (!content.headline) { alert(`Unable to ${verb}: Headline required.`); return false; }
  if (!content.sections.length) { alert(`Unable to ${verb}: Article must have at least one paragraph.`); return false; }
  if (content.sections.some(s => !s.text)) { alert(`Unable to ${verb}: Article cannot contain empty paragraphs.`); return false; }
  if (content.sections.some(s => s.text.length < 40)) { alert(`Unable to ${verb}: Paragraphs must be at least 40 characters.`); return false; }
  if (content.sections.some(s => s.text.length > 500)) { alert(`Unable to ${verb}: Paragraphs must be no longer than 500 characters.`); return false; }
  if (content.sections.some(s => s.includeImage && !s.image)) { alert(`Unable to ${verb}: All paragraphs must contain a photo.`); return false; }
  if (content.sections.some(s => s.includeImage && s.alt === 'invalid')) { alert(`Unable to ${verb}: All photos must be of students, staff, or student work.`); return false; }
  if (content.sections.some(s => s.includeImage && !s.alt)) { alert(`Unable to ${verb}: All photos must include a description.`); return false; }
  if (content.sections.some(s => s.includeImage && !s.consent)) { alert(`Unable to ${verb}: All photos must be ticked for photo consent. You can blur faces if needed.`); return false; }
  for (const word of bannedWords) {
    if (content.sections.some(s => s.text.toLowerCase().includes(word))) { alert(`Unable to ${verb}: Avoid using the word "${word}".`); return false; }
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
      [...document.querySelectorAll('#headline,.section-text,.alt-text')].forEach(el => el.contentEditable = true);
      [...document.querySelectorAll('.section-delete,.clear-image,.blur-image-section,#addsectioncontainer,#submit')].forEach(el => el.style.display = '');
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
    [...document.querySelectorAll('#headline,.section-text,.alt-text')].forEach(el => el.contentEditable = false);
    [...document.querySelectorAll('.section-delete,.clear-image,.blur-image-section,#addsectioncontainer')].forEach(el => el.style.display = 'none');
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
        const width = Math.round(event.detail.width);
        const height = Math.round(event.detail.height);

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
  const imageSection = currentImageInputElement.closest('.section-image');
  var blobUrl = URL.createObjectURL(blob);
  setImage(imageSection, blobUrl, 'generating');
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
  const describeResp = await request(`/api/articles/${articleKey}/image/${imageUrl}/describe`, 'POST');
  if (describeResp.ok) {
    const description = await describeResp.json();
    setImage(imageSection, imageUrl, description);
  } else {
    setImage(imageSection, imageUrl);
  }
  URL.revokeObjectURL(blobUrl);
  await save();
}

// Image blurring

const canvas = document.getElementById('blur-canvas');
const ctx = canvas.getContext('2d');
const prevVals = [];
let blurImageSection;
let blurCurrentImageName;
let fixedBlurs;
let blurRadius;
let blurImage;

function openBlurPanel(imageSection, imageName) {
  blurImageSection = imageSection;
  blurCurrentImageName = imageName;
  fixedBlurs = [];
  blurRadius = 50;
  blurImage = new Image();
  blurImage.crossOrigin = 'anonymous';
  blurImage.onload = () => ctx.drawImage(blurImage, 0, 0, canvas.width, canvas.height);
  blurImage.src = `${blobBaseUrl}${domain}/${articleKey}/${imageName}?${sas}`;
  document.getElementById('blur-editor').style.display = 'block';
}

canvas.addEventListener('mousemove', (event) => {
  const rect = canvas.getBoundingClientRect();
  currentX = event.clientX - rect.left;
  currentY = event.clientY - rect.top;
  redrawCanvas();
  drawCircularBlur(currentX, currentY, blurRadius);
});

canvas.addEventListener('mouseleave', () => {
  redrawCanvas();
});

canvas.addEventListener('click', () => {
  fixedBlurs.push({ x: currentX, y: currentY, radius: blurRadius });
  redrawCanvas();
});

canvas.addEventListener('contextmenu', (event) => {
  fixedBlurs.pop();
  redrawCanvas();
  event.preventDefault();
});

canvas.addEventListener('wheel', (event) => {
  blurRadius += event.deltaY * -0.05;
  blurRadius = Math.max(10, Math.min(200, blurRadius));
  redrawCanvas();
  drawCircularBlur(currentX, currentY, blurRadius);
  event.preventDefault();
});

document.addEventListener('keydown', (event) => {
  if (event.target.matches('input')) return;
  if (event.key === '-') {
    blurRadius = Math.max(10, blurRadius - 2);
    redrawCanvas();
    drawCircularBlur(currentX, currentY, blurRadius);
  } else if (event.key === '=' || event.key === '+') {
    blurRadius = Math.min(200, blurRadius + 2);
    redrawCanvas();
    drawCircularBlur(currentX, currentY, blurRadius);
  }
});

function redrawCanvas() {
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.drawImage(blurImage, 0, 0, canvas.width, canvas.height);
  fixedBlurs.forEach(({ x, y, radius }) => drawCircularBlur(x, y, radius));
}

function drawCircularBlur(x, y, radius) {
  ctx.save();
  ctx.beginPath();
  ctx.arc(x, y, radius, 0, Math.PI * 2);
  ctx.clip();
  ctx.filter = 'blur(10px)';
  ctx.drawImage(canvas, 0, 0);
  ctx.restore();
}

document.getElementById('blur-save').addEventListener('click', async () => {
  if (fixedBlurs.length === 0) {
    alert('You have not selected any regions to blur.');
    return;
  }
  if (!confirm('Are you sure you want to blur the selected regions? This cannot be undone.')) return;
  redrawCanvas();
  const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.9));
  const resp = await fetch(`/api/articles/${articleKey}/image`, {
    method: 'POST',
    headers: { 'X-XSRF-TOKEN': antiforgeryToken, 'Content-Type': 'image/jpeg' },
    body: blob
  });
  if (!resp.ok) {
    alert(await resp.json());
    return;
  }
  const imageUrl = await resp.json();
  blurImageSection.dataset.image = imageUrl;
  blurImageSection.querySelector('img').src = `${blobBaseUrl}${domain}/${articleKey}/${imageUrl}?${sas}`;
  await save();
  await request(`/api/articles/${articleKey}/image/${blurCurrentImageName}`, 'DELETE');
  document.getElementById('blur-editor').style.display = 'none';
});

document.getElementById('blur-cancel').addEventListener('click', () => {
  document.getElementById('blur-editor').style.display = 'none';
});

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

document.getElementById('requestaifeedback').addEventListener('click', async e => {
  if (e.target.classList.contains('disabled')) return;
  if (!isValid('request review')) return;
  e.target.classList.add('disabled');
  const div = document.getElementById('aifeedbackcontent');
  div.innerHTML = '';
  document.getElementById('aifeedbackbubble').style.display = '';
  document.getElementById('aifeedback').style.display = '';
  document.getElementById('aifeedbackfinish').style.display = 'none';
  window.scrollTo(0, document.body.scrollHeight);
  const resp = await request(`/api/aifeedback`, 'POST', {
    Identifier: articleKey,
    Headline: content.headline,
    Content: content.sections.map(s => s.text + '\n' + (s.includeImage ? (s.altText ? `[Photo - Caption: ${s.altText}]` : '[Photo]') : '')).join('\n')
  });
  const reader = resp.body.getReader();
  const decoder = new TextDecoder();
  let feedback = '';
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    if (!value) continue;
    const token = decoder.decode(value);
    feedback += token;
    const html = feedback.split('\n')
      .filter(o => o)
      .map(o => o.slice(2).trim())
      .map(o => `<li>${o}</li>`)
      .join('');
    div.innerHTML = '<ul>' + html + '</ul>';
  }
  document.getElementById('aifeedbackbubble').style.display = 'none';
  document.getElementById('aifeedbackfinish').style.display = '';
  document.getElementById('submit').style.display = '';
});

// AI writing

document.addEventListener('keydown', function (e) {
  if (e.altKey) {
    if (e.key === '\\') {
      if (content === null || (content.headline === '' && content.sections.every(s => s.text === '' && s.image === null))) document.getElementById('aiwriting').style.display = '';
      e.preventDefault();
    }
  }
});

[document.getElementById('aitopic'), document.getElementById('aicontent')].forEach(el => {
  el.addEventListener('blur', e => {
    e.target.innerText = e.target.innerText.replace(invalidCharacters, '').replace(/\n/g, '; ').replace(invalidWhitespace, ' ').replace(repeatedSpaces, ' ').trim();
  });
});

document.getElementById('aiparagraphs').addEventListener('blur', e => {
  const num = parseInt(e.target.innerText);
  e.target.innerText = (isNaN(num) || num < 1) ? '1' : (num > 10 ? '10' : num);
});

document.getElementById('aiwrite').addEventListener('click', async () => {
  const topic = document.getElementById('aitopic').innerText;
  const content = document.getElementById('aicontent').innerText;
  if (!topic) { alert('Please specify the article topic.'); return; }
  if (content.length < 100) { alert('Please provide more information, so the AI can write a meaningful article.'); return; }
  [...document.getElementById('aiwriting').children].forEach(el => el.style.display = 'none');
  document.getElementById('ailoading').style.display = null;
  const response = await request(`/api/aiwrite`, 'POST', {
    Identifier: articleKey,
    Headline: topic,
    Content: content,
    Paragraphs: parseInt(document.getElementById('aiparagraphs').innerText)
  });
  if (response.ok) {
    const data = await response.json();
    headline.innerText = data.headline;
    article.innerHTML = '';
    for (const paragraph of data.body) addSection(true, paragraph, null, null, false, null);
  }
  document.getElementById('aiwriting').style.display = 'none';
  [...document.getElementById('aiwriting').children].forEach(el => el.style.display = null);
  document.getElementById('ailoading').style.display = 'none';
  await save();
  [...document.querySelectorAll('#headline, .section-text')].forEach(el => flash(el));

});

document.getElementById('aicancel').addEventListener('click', async () => {
  if (document.getElementById('aitopic').innerText || document.getElementById('aicontent').innerText) {
    if (!confirm('Are you sure you want to close the AI panel?')) return;
  }
  document.getElementById('aiwriting').style.display = 'none';
});