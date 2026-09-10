// Globals

const article = document.getElementsByTagName('article')[0];
const headline = document.getElementById('headline');
const articleTitle = document.getElementById('article-title');
const articleContributors = document.getElementById('article-contributors');
const invalidCharacters = /[^A-Za-z0-9\[\]\*!"£$%&();:',.=_#@\/\\?\s\u00E0\u00E2\u00E6\u00E7\u00E9\u00E8\u00EA\u00EB\u00EE\u00EF\u00F4\u0153\u00F9\u00FB\u00FC\u00FF\u00C0\u00C2\u00C6\u00C7\u00C9\u00C8\u00CA\u00CB\u00CE\u00CF\u00D4\u0152\u00D9\u00DB\u00DC\u0178\u00A1\u00BF\+-]+/g;
const invalidWhitespace = /[\f\t\v\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff]/g;
const repeatedSpaces = /\s\s+/g;

let inputsIdx = 0;
let saved = true;
let editVersion = 0;
let saveQueue = Promise.resolve(true);
let activeSection = null;
let toolbarOrigin = null;

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
if (!article.childElementCount && !isApproved && (!isSubmitted || isEditor)) addSection(!isIntro);
configureInput([document.getElementById('headline')]);
if (isEditor) {
  const metadataValues = new Map([[articleTitle, articleTitle.innerText], [articleContributors, articleContributors.innerText]]);
  for (const el of [articleTitle, articleContributors]) {
    el.addEventListener('input', () => { saved = false; editVersion++; });
    el.addEventListener('blur', async e => {
      if (saved) return;
      if (e.target === articleTitle)
        e.target.innerText = e.target.innerText.replace(/\s+/g, ' ').trim();
      else
        e.target.innerText = e.target.innerText.split(',').map(o => o.trim().toLowerCase()).join(',');
      await queueSave(async () => {
        if (e.target.innerText === metadataValues.get(e.target)) return true;
        const title = articleTitle.innerText;
        const contributors = articleContributors.innerText;
        const resp = await request(`/api/articles/${articleKey}/metadata`, 'PUT', { title, contributors, etag });
        if (!resp.ok) {
          e.target.innerText = metadataValues.get(e.target);
          window.location.reload();
          return false;
        }
        etag = await resp.json();
        metadataValues.set(articleTitle, title);
        metadataValues.set(articleContributors, contributors);
        flash(e.target);
        return true;
      });
    });
  }
}
if (isSubmitted) {
  lockEditing();
} else {
  document.getElementById('submit').style.display = 'none';
  if (isEditor) document.getElementById('approve').style.display = 'none';
}
document.getElementById('main').style.display = '';
updateCoverPhotoLinks();

// Add section

function addSection(includeImage, text, image, alt, consent, before = null) {
  const section = document.getElementById('sectiontemplate').content.firstElementChild.cloneNode(true);
  article.insertBefore(section, before);
  section.dataset.includeimage = includeImage ? 'true' : 'false';
  const textElement = section.querySelector('.section-text');
  configureInput([textElement]);
  if (text) textElement.innerText = text;
  const imageElement = section.querySelector('.section-image');
  imageElement.hidden = !includeImage;
  const fileInput = imageElement.querySelector('input[type="file"]');
  fileInput.addEventListener('click', () => {
    imageElement.dataset.pendingimage = 'true';
    updateSectionControls();
  });
  fileInput.addEventListener('cancel', () => {
    resetImageInput(fileInput);
  });
  fileInput.addEventListener('change', onSelectImage);
  imageElement.querySelector('input[type="checkbox"]').addEventListener('change', async e => {
    if (!await save()) return;
    flash(e.target.parentNode.parentNode);
  });
  configureInput([imageElement.querySelector('.alt-text')]);
  setImage(imageElement, image, alt, consent);
  return section;
}

function updateSectionControls() {
  const locked = isApproved || (isSubmitted && !isEditor);
  if (locked || !activeSection?.isConnected) setActiveSection(null);
  for (const section of article.children) {
    section.querySelector('.section-menu').hidden = locked;
    section.querySelector('.section-up').disabled = !section.previousElementSibling;
    section.querySelector('.section-down').disabled = !section.nextElementSibling;
    const insert = section.querySelector('.section-add');
    const insertParent = section.dataset.includeimage === 'true' ? section : section.querySelector('.section-menu');
    if (insert.parentNode !== insertParent) insertParent.append(insert);
    insert.hidden = locked;
    const toggle = section.querySelector('.section-toggle-image');
    if (!toggle) continue;
    const imageElement = section.querySelector('.section-image');
    toggle.hidden = !isEditor || !!imageElement.dataset.pendingimage || !!imageElement.querySelector('img').getAttribute('src');
    toggle.title = section.dataset.includeimage === 'true' ? 'Use text only' : 'Add image space';
    toggle.setAttribute('aria-label', toggle.title);
    toggle.querySelector('.material-symbols-outlined').textContent = section.dataset.includeimage === 'true' ? 'image_not_supported' : 'image';
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
  const isDuplicate = alt === 'duplicate';
  const isRejected = isInvalid || isDuplicate;

  el.dataset.image = (image && image.substring(0, 5) !== 'blob:') ? image : '';

  uploadElement.style.display = image ? 'none' : null;
  imageElement.src = image ? (image.substring(0, 5) === 'blob:' ? image : `${blobBaseUrl}${articleKey}/${image}?${sas}`) : '';
  imageElement.style.display = image ? null : 'none';
  altTextElement.contentEditable = !isGenerating && !isRejected;
  altTextElement.className = isGenerating ? 'alt-text alt-text-loading' : (isRejected ? 'alt-text alt-text-error' : 'alt-text');
  altTextElement.dataset.rejection = isRejected ? alt : '';
  if (isGenerating) altTextElement.innerHTML = '<div class="typing" title="Generating caption..."><span></span><span></span><span></span></div>';
  else altTextElement.innerText = isInvalid ? 'Only photos of students, staff, or student work are accepted. Please upload a different photo.' : (isDuplicate ? 'This photo has been used before. Please upload a different photo.' : (alt ?? ''));
  altTextElement.style.display = image ? null : 'none';
  consentAreaElement.style.display = image && !isGenerating && !isRejected ? null : 'none';
  consentElement.checked = image ? consent : false;
  clearElement.style.display = image && !isGenerating ? null : 'none';
  blurImageElement.style.display = image && !isGenerating && !isRejected ? null : 'none';
  approveElement.style.display = image && isRejected && isAdmin ? null : 'none';
  setCoverPhotoElement.style.display = 'none';
  updateSectionControls();
}

// Configure input constraints and autosave

function queueSave(action) {
  const version = ++editVersion;
  saved = false;
  saveQueue = saveQueue.then(async success => {
    if (!success) return false;
    const result = await action();
    if (result && version === editVersion) saved = true;
    return result;
  }).catch(() => {
    alert('Unable to save changes. Please reload the page before continuing.');
    return false;
  });
  return saveQueue;
}

function save() {
  return queueSave(async () => {
    content = {
      headline: headline.innerText,
      sections: [...article.getElementsByTagName('section')].map(el => {
        const imageSection = el.getElementsByClassName('section-image')[0];
        const includeImage = el.dataset.includeimage === 'true';
        const imageUrl = includeImage && imageSection.dataset.image ? imageSection.dataset.image : null;
        const alt = imageUrl ? imageSection.querySelector('.alt-text') : null;
        const isAltTextLoading = imageUrl ? alt.classList.contains('alt-text-loading') : null;
        const altText = imageUrl ? (alt.dataset.rejection || (isAltTextLoading ? '' : alt.innerText)) : null;
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
      window.location.reload();
      return false;
    }
    document.getElementById('requestaifeedback').classList.remove('disabled');
    etag = await resp.json();
    return true;
  });
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
      editVersion++;
    });

    el.addEventListener('blur', async e => {
      if (saved) return;
      e.target.innerText = e.target.innerText.replace(invalidWhitespace, ' ').replace(repeatedSpaces, ' ').trim();
      if (!await save()) return;
      flash(e.target);
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

function setActiveSection(section) {
  if (section === activeSection) return;
  activeSection?.classList.remove('section-active');
  activeSection = section;
  activeSection?.classList.add('section-active');
  toolbarOrigin = null;
}

document.addEventListener('pointermove', e => {
  if (e.pointerType === 'touch') return;
  if (activeSection?.isConnected) {
    const menu = activeSection.querySelector('.section-menu');
    const controls = [...activeSection.querySelectorAll(':scope > .section-menu,:scope > .section-add')];
    const sectionRect = activeSection.getBoundingClientRect();
    if (!menu.hidden) {
      if (e.clientX >= sectionRect.left && e.clientX <= sectionRect.right && e.clientY >= sectionRect.top && e.clientY <= sectionRect.bottom) {
        toolbarOrigin = { x: e.clientX, y: e.clientY };
        return;
      }
      if (controls.some(control => control.contains(e.target))) {
        toolbarOrigin = { x: e.clientX, y: e.clientY };
        return;
      }
      if (toolbarOrigin) {
        // Keep direct paths to either the toolbar or the separate image add button open.
        for (const control of controls) {
          const rect = control.getBoundingClientRect();
          let enter = 1;
          let leave = Infinity;
          for (const [position, origin, min, max] of [
            [e.clientX, toolbarOrigin.x, rect.left - 2, rect.right + 2],
            [e.clientY, toolbarOrigin.y, rect.top - 2, rect.bottom + 2]
          ]) {
            const delta = position - origin;
            if (delta === 0) {
              if (origin < min || origin > max) leave = -1;
            } else {
              enter = Math.max(enter, Math.min((min - origin) / delta, (max - origin) / delta));
              leave = Math.min(leave, Math.max((min - origin) / delta, (max - origin) / delta));
            }
          }
          if (enter <= leave) return;
        }
      }
    }
  }
  const section = e.target.closest('section');
  setActiveSection(section?.parentElement === article && !section.querySelector('.section-menu').hidden ? section : null);
  if (activeSection) toolbarOrigin = { x: e.clientX, y: e.clientY };
});

document.addEventListener('pointerdown', e => {
  const section = e.target.closest('section');
  setActiveSection(section?.parentElement === article && !section.querySelector('.section-menu').hidden ? section : null);
});

document.addEventListener('pointerleave', () => setActiveSection(null));
article.addEventListener('focusin', e => setActiveSection(e.target.closest('section')));

article.addEventListener('click', async e => {
  if ((isApproved || (isSubmitted && !isEditor)) && !e.target.classList.contains('set-cover-photo')) return;
  const section = e.target.closest('section');
  if (!section) return;
  if (e.target.matches('.section-up,.section-down')) {
    const up = e.target.classList.contains('section-up');
    const adjacent = up ? section.previousElementSibling : section.nextElementSibling;
    if (!adjacent) return;
    article.insertBefore(section, up ? adjacent : adjacent.nextElementSibling);
    updateSectionControls();
    (e.target.disabled ? section.querySelector(up ? '.section-down' : '.section-up') : e.target).focus();
    await save();
  }
  else if (e.target.classList.contains('section-add')) {
    const added = addSection(true, null, null, null, false, section.nextElementSibling);
    added.querySelector('.section-text').focus();
    await save();
  }
  else if (e.target.classList.contains('section-toggle-image')) {
    const imageElement = section.querySelector('.section-image');
    if (!isEditor || imageElement.dataset.pendingimage || imageElement.querySelector('img').getAttribute('src')) return;
    const includeImage = section.dataset.includeimage !== 'true';
    section.dataset.includeimage = includeImage ? 'true' : 'false';
    imageElement.hidden = !includeImage;
    updateSectionControls();
    await save();
  }
  else if (e.target.classList.contains('section-delete')) {
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
    if (!article.childElementCount) addSection(!isIntro);
    updateSectionControls();
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
  if (content.sections.some(s => s.includeImage && s.alt === 'duplicate')) { alert(`Unable to ${verb}: Photos cannot duplicate previously used photos.`); return false; }
  if (content.sections.some(s => s.includeImage && !s.alt)) { alert(`Unable to ${verb}: All photos must include a description.`); return false; }
  if (content.sections.some(s => s.includeImage && !s.consent)) { alert(`Unable to ${verb}: All photos must be ticked for photo consent. You can blur faces if needed.`); return false; }
  for (const word of bannedWords) {
    if (content.sections.some(s => s.text.toLowerCase().includes(word))) { alert(`Unable to ${verb}: Avoid using the word "${word}".`); return false; }
  }
  return true;
}

document.getElementById('submit').addEventListener('click', async () => {
  if (!await saveQueue) return;
  if (isSubmitted) {
    if (!isEditor) return;
    if (!confirm('Are you sure you want to unsubmit this article?')) return;
    const resp = await request(`/api/articles/${articleKey}/unsubmit`, 'POST', { etag });
    etag = await resp.json();
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
  const resp = await request(`/api/articles/${articleKey}/submit`, 'POST', { etag });
  etag = await resp.json();
  isSubmitted = true;
  lockEditing();
});

if (isEditor) {
  document.getElementById('approve').addEventListener('click', async () => {
    if (!await saveQueue) return;
    if (isApproved) {
      const resp = await request(`/api/articles/${articleKey}/unapprove`, 'POST', { etag });
      etag = await resp.json();
      const button = document.getElementById('approve');
      button.innerText = 'Approve article';
      button.classList.remove('disabled');
      button.style.cursor = '';
      isApproved = false;
      [...document.querySelectorAll('#article-title,#article-contributors,#headline,.section-text,.alt-text')].forEach(el => el.contentEditable = true);
      [...document.querySelectorAll('.section-delete,.clear-image,.blur-image-section,#submit')].forEach(el => el.style.display = '');
      [...document.querySelectorAll('.section-text,.section-image,#headline')].forEach(el => el.style.border = '');
      [...document.querySelectorAll('input')].forEach(el => el.disabled = false);
      updateCoverPhotoLinks();
      updateSectionControls();
      document.getElementById('requestaifeedback').style.display = '';
      return;
    }
    if (content.sections.some(s => s.includeImage && !s.consent)) { alert('Unable to approve: some students do not have photo consent.'); return; }
    const resp = await request(`/api/articles/${articleKey}/approve`, 'POST', { etag });
    etag = await resp.json();
    isApproved = true;
    lockEditing();
  });
}

function lockEditing() {
  updateSectionControls();
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
    [...document.querySelectorAll('#article-title,#article-contributors,#headline,.section-text,.alt-text')].forEach(el => el.contentEditable = false);
    [...document.querySelectorAll('.section-delete,.clear-image,.blur-image-section')].forEach(el => el.style.display = 'none');
    [...document.querySelectorAll('.section-text,.section-image,#headline')].forEach(el => el.style.border = 'none');
    [...document.querySelectorAll('input')].forEach(el => el.disabled = true);
    
    button.classList.add('disabled');
  }
}

// Image cropping and uploading

function resetImageInput(input) {
  input.value = null;
  delete input.closest('.section-image').dataset.pendingimage;
  updateSectionControls();
}

let cropper;
let cropperCanvas;
let cropperImage;
let cropperSelection;
let currentImageInputElement;
let resettingCropperSelection = false;
let cropperResizeStart;

const cropperResizeObserver = new ResizeObserver(() => {
  cropperSelection.$clear();
  cropperImage.$center('contain');
  resetCropperSelection();
});

const cropperTemplate = `
  <cropper-canvas background>
    <cropper-image initial-center-size="contain" rotatable scalable translatable></cropper-image>
    <cropper-shade hidden></cropper-shade>
    <cropper-handle action="move" plain></cropper-handle>
    <cropper-selection initial-coverage="0.9" aspect-ratio="1.7777777777777777" movable resizable precise>
      <cropper-grid role="grid" bordered covered></cropper-grid>
      <cropper-crosshair centered></cropper-crosshair>
      <cropper-handle action="move" plain></cropper-handle>
      <cropper-handle action="n-resize"></cropper-handle>
      <cropper-handle action="e-resize"></cropper-handle>
      <cropper-handle action="s-resize"></cropper-handle>
      <cropper-handle action="w-resize"></cropper-handle>
      <cropper-handle action="ne-resize"></cropper-handle>
      <cropper-handle action="nw-resize"></cropper-handle>
      <cropper-handle action="se-resize"></cropper-handle>
      <cropper-handle action="sw-resize"></cropper-handle>
    </cropper-selection>
  </cropper-canvas>`;

function isSelectionWithin(selection, bounds) {
  return selection.x >= bounds.x - 0.5
    && selection.y >= bounds.y - 0.5
    && selection.x + selection.width <= bounds.x + bounds.width + 0.5
    && selection.y + selection.height <= bounds.y + bounds.height + 0.5;
}

function getImageBounds(imageRect, canvasRect) {
  return {
    x: imageRect.left - canvasRect.left,
    y: imageRect.top - canvasRect.top,
    width: imageRect.width,
    height: imageRect.height
  };
}

function resetCropperSelection() {
  const canvasRect = cropperCanvas.getBoundingClientRect();
  const imageBounds = getImageBounds(cropperImage.getBoundingClientRect(), canvasRect);
  const x = Math.max(0, imageBounds.x);
  const y = Math.max(0, imageBounds.y);
  const availableWidth = Math.min(canvasRect.width, imageBounds.x + imageBounds.width) - x;
  const availableHeight = Math.min(canvasRect.height, imageBounds.y + imageBounds.height) - y;
  const maxWidth = Math.min(availableWidth, availableHeight * 16 / 9);
  const scale = Math.hypot(...cropperImage.$getTransform().slice(0, 2));
  const width = Math.min(maxWidth, Math.max(maxWidth * 0.9, 1120 * scale));
  const height = width * 9 / 16;

  resettingCropperSelection = true;
  cropperSelection.$change(
    x + (availableWidth - width) / 2,
    y + (availableHeight - height) / 2,
    width,
    height
  );
  resettingCropperSelection = false;
}

function onCropperSelectionChange(event) {
  if (resettingCropperSelection || event.detail.width === 0 || event.detail.height === 0) return;
  const canvasRect = cropperCanvas.getBoundingClientRect();
  const canvasBounds = { x: 0, y: 0, width: canvasRect.width, height: canvasRect.height };
  const imageBounds = getImageBounds(cropperImage.getBoundingClientRect(), canvasRect);
  const scale = Math.hypot(...cropperImage.$getTransform().slice(0, 2));

  if (!isSelectionWithin(event.detail, canvasBounds)
    || !isSelectionWithin(event.detail, imageBounds)
    || event.detail.width / scale < 1119.5
    || event.detail.height / scale < 629.5)
    event.preventDefault();
}

function onCropperResize(event) {
  const { action, startX, startY, endX, endY } = event.detail;
  if (!action.endsWith('-resize')) return;
  event.preventDefault();
  cropperResizeStart ??= {
    action,
    startX,
    startY,
    x: cropperSelection.x,
    y: cropperSelection.y,
    width: cropperSelection.width,
    height: cropperSelection.height
  };
  const start = cropperResizeStart;
  const direction = start.action.split('-')[0];
  const west = direction.includes('w');
  const east = direction.includes('e');
  const north = direction.includes('n');
  const south = direction.includes('s');
  const ratio = cropperSelection.aspectRatio;
  const anchorX = start.x + (west ? start.width : east ? 0 : start.width / 2);
  const anchorY = start.y + (north ? start.height : south ? 0 : start.height / 2);
  const canvasRect = cropperCanvas.getBoundingClientRect();
  const bounds = getImageBounds(cropperImage.getBoundingClientRect(), canvasRect);
  const left = Math.max(0, bounds.x);
  const top = Math.max(0, bounds.y);
  const right = Math.min(canvasRect.width, bounds.x + bounds.width);
  const bottom = Math.min(canvasRect.height, bounds.y + bounds.height);
  const maxWidth = Math.min(
    west ? anchorX - left : east ? right - anchorX : 2 * Math.min(anchorX - left, right - anchorX),
    (north ? anchorY - top : south ? bottom - anchorY : 2 * Math.min(anchorY - top, bottom - anchorY)) * ratio
  );
  const scale = Math.hypot(...cropperImage.$getTransform().slice(0, 2));
  const delta = west || east ? (endX - start.startX) * (west ? -1 : 1) : (endY - start.startY) * (north ? -ratio : ratio);
  const width = Math.min(maxWidth, Math.max(1120 * scale, start.width + delta));
  const height = width / ratio;
  cropperSelection.$change(
    anchorX - (west ? width : east ? 0 : width / 2),
    anchorY - (north ? height : south ? 0 : height / 2),
    width,
    height
  );
}

function onCropperImageTransform(event) {
  if (cropperSelection.hidden) return;
  const canvasRect = cropperCanvas.getBoundingClientRect();
  const imageClone = cropperImage.cloneNode();
  imageClone.style.transform = `matrix(${event.detail.matrix.join(', ')})`;
  imageClone.style.opacity = 0;
  cropperCanvas.appendChild(imageClone);
  const imageRect = imageClone.getBoundingClientRect();
  imageClone.remove();

  const isSmallerThanCanvas = (imageRect.top > canvasRect.top + 0.5 && imageRect.right < canvasRect.right - 0.5)
    || (imageRect.right < canvasRect.right - 0.5 && imageRect.bottom < canvasRect.bottom - 0.5)
    || (imageRect.bottom < canvasRect.bottom - 0.5 && imageRect.left > canvasRect.left + 0.5)
    || (imageRect.left > canvasRect.left + 0.5 && imageRect.top > canvasRect.top + 0.5);

  if (isSmallerThanCanvas) {
    event.preventDefault();
    return;
  }

  if (!cropperSelection.hidden) {
    const imageBounds = getImageBounds(imageRect, canvasRect);
    const scale = Math.hypot(...event.detail.matrix.slice(0, 2));
    if (!isSelectionWithin(cropperSelection, imageBounds)
      || cropperSelection.width / scale < 1119.5
      || cropperSelection.height / scale < 629.5)
      event.preventDefault();
  }
}

function onSelectImage(e) {
  if (!e.target.files || e.target.files.length === 0 || !e.target.files[0]) {
    resetImageInput(e.target);
    return;
  }
  const type = e.target.files[0].type;
  if (type !== 'image/jpeg' && type !== 'image/png') {
    alert('Only JPG and PNG images are supported.');
    resetImageInput(e.target);
    return;
  }
  e.target.closest('.section-image').dataset.pendingimage = 'true';
  updateSectionControls();
  const image = document.getElementById('cropper-image');
  image.onload = async () => {
    if (image.naturalWidth < 1120 || image.naturalHeight < 630) {
      alert('Please select a higher-quality photo.\nThe image must be at least 1120 x 630 pixels.');
      resetImageInput(e.target);
      return;
    }
    currentImageInputElement = e.target;
    if (image.naturalWidth === 1120 && image.naturalHeight === 630) {
      await uploadImage(e.target.files[0], e.target.files[0].type);
      return;
    }
    document.getElementById('image-editor').style.display = 'grid';
    cropper = new Cropper.default(image, {
      template: cropperTemplate
    });
    cropperCanvas = cropper.getCropperCanvas();
    cropperImage = cropper.getCropperImage();
    cropperSelection = cropper.getCropperSelection();
    await cropperImage.$ready();
    cropperSelection.$clear();
    cropperImage.$center('contain');
    resetCropperSelection();
    cropperImage.addEventListener('transform', onCropperImageTransform);
    cropperSelection.addEventListener('change', onCropperSelectionChange);
    cropperCanvas.addEventListener('actionstart', () => { cropperResizeStart = null; });
    cropperCanvas.addEventListener('action', onCropperResize, true);
    cropperResizeObserver.observe(cropperCanvas);
    cropperResizeObserver.observe(cropperImage);
    document.getElementById('image-editor').style.opacity = 1;
  }
  image.onerror = () => {
    alert('Unable to load this photo. Please select another image.');
    resetImageInput(e.target);
  };
  image.src = URL.createObjectURL(e.target.files[0]);
}

document.getElementById('image-cancel').addEventListener('click', () => {
  cropperResizeObserver.disconnect();
  const editor = document.getElementById('image-editor');
  editor.style.display = 'none';
  editor.style.opacity = 0;
  cropper.destroy();
  resetImageInput(currentImageInputElement);
  currentImageInputElement = null;
});

document.getElementById('image-upload').addEventListener('click', async () => {
  const canvas = await cropperSelection.$toCanvas({ width: 1120, height: 630 });
  const type = currentImageInputElement.files[0].type;
  const blob = await new Promise(resolve => canvas.toBlob(resolve, type, 0.9));
  cropperResizeObserver.disconnect();
  const editor = document.getElementById('image-editor');
  editor.style.display = 'none';
  editor.style.opacity = 0;
  await uploadImage(blob, type);
  cropper.destroy();
});

document.getElementById('rotate-left').addEventListener('click', () => {
  cropperSelection.$clear();
  cropperImage.$rotate('-90deg').$center('contain');
  resetCropperSelection();
});
document.getElementById('rotate-right').addEventListener('click', () => {
  cropperSelection.$clear();
  cropperImage.$rotate('90deg').$center('contain');
  resetCropperSelection();
});

async function uploadImage(blob, type) {
  const input = currentImageInputElement;
  const imageSection = input.closest('.section-image');
  const blobUrl = URL.createObjectURL(blob);
  setImage(imageSection, blobUrl, 'generating');
  try {
    const resp = await fetch(`/api/articles/${articleKey}/image`, {
      method: 'POST',
      headers: { 'X-XSRF-TOKEN': antiforgeryToken, 'Content-Type': type },
      body: blob
    });
    if (!resp.ok) {
      alert(await resp.json());
      setImage(imageSection, null);
      return;
    }
    const imageUrl = await resp.json();
    setImage(imageSection, imageUrl, 'generating');
    const describeResp = await request(`/api/articles/${articleKey}/image/${imageUrl}/describe`, 'POST');
    setImage(imageSection, imageUrl, describeResp.ok ? await describeResp.json() : null);
    await save();
  } catch {
    alert('Unable to finish uploading this photo. Please try again.');
    setImage(imageSection, imageSection.dataset.image);
    if (imageSection.dataset.image) await save();
  } finally {
    URL.revokeObjectURL(blobUrl);
    resetImageInput(input);
  }
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
  blurImage.src = `${blobBaseUrl}${articleKey}/${imageName}?${sas}`;
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
  if (document.getElementById('blur-editor').style.display !== 'block') return;
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
  const previousDescription = blurImageSection.querySelector('.alt-text').innerText;
  const consent = blurImageSection.querySelector('input[type="checkbox"]').checked;
  setImage(blurImageSection, imageUrl, 'generating', consent);
  const duplicateResp = await request(`/api/articles/${articleKey}/image/${imageUrl}/duplicate/${blurCurrentImageName}`, 'POST');
  if (!duplicateResp.ok) {
    setImage(blurImageSection, blurCurrentImageName, previousDescription, consent);
    await request(`/api/articles/${articleKey}/image/${imageUrl}`, 'DELETE');
    return;
  }
  const isDuplicate = await duplicateResp.json();
  setImage(blurImageSection, imageUrl, isDuplicate ? 'duplicate' : previousDescription, consent);
  if (!await save()) return;
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
