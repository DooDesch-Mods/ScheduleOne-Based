const $ = id => document.getElementById(id);
let current = null;
let drawn = '';

function div(className, text) {
  const node = document.createElement('div');
  node.className = className;
  if (text) node.textContent = text;
  return node;
}

function place(node, left, top, width, height) {
  node.style.left = left + 'px';
  node.style.top = top + 'px';
  node.style.width = width + 'px';
  node.style.height = height + 'px';
  return node;
}

function drawSteps(steps) {
  const host = $('steps');
  host.replaceChildren();
  for (const step of steps) {
    const node = div('step ' + step.state);
    node.appendChild(div('step-bar'));
    node.appendChild(div('step-label', step.label));
    host.appendChild(node);
  }
}

function drawPlan(plan) {
  const key = JSON.stringify(plan);
  if (key === drawn) return;
  drawn = key;
  const host = $('drawing');
  host.replaceChildren();
  host.style.width = plan.width + 'px';
  host.style.height = plan.height + 'px';
  for (let x = plan.metre; x < plan.width; x += plan.metre) host.appendChild(place(div('grid-line'), x, 0, 1, plan.height));
  for (let y = plan.metre; y < plan.height; y += plan.metre) host.appendChild(place(div('grid-line'), 0, y, plan.width, 1));
  for (const room of plan.rooms) host.appendChild(place(div('room ' + room.kind, room.label), room.left, room.top, room.width, room.height));
}

function render(raw) {
  if (raw === null || raw === undefined) { console.error('The construction plan returned no state.'); return; }
  try {
    const data = JSON.parse(raw);
    current = data;
    $('title').textContent = data.title;
    $('close').textContent = data.close;
    $('status').textContent = data.status;
    $('progress').textContent = data.progress;
    $('summary').textContent = data.summary;
    $('rights-label').textContent = data.rightsLabel;
    $('rights').textContent = data.rights;
    $('construction-label').textContent = data.constructionLabel;
    $('construction').textContent = data.construction;
    $('cash').textContent = data.cash;
    $('notice').textContent = data.notice;
    $('payment').textContent = data.payment;
    $('buy').textContent = data.buy;
    $('buy').disabled = !data.canBuy;
    $('buy').style.display = data.ordering ? 'flex' : 'none';
    const restrictions = $('restrictions');
    restrictions.replaceChildren();
    for (const line of data.restrictions) restrictions.appendChild(div('restriction', line));
    drawSteps(data.steps);
    drawPlan(data.drawing);
  } catch (error) { console.error('Construction plan render failed: ' + error.message); }
}

$('buy').addEventListener('click', () => {
  if (!current || !current.canBuy) return;
  const answer = s1.call('plan.buy', current.stage === 'RightsMissing' ? 'rights' : 'construction');
  if (answer === null || answer === undefined) console.error('The purchase request returned no acknowledgement.');
  render(s1.call('plan.state'));
});
$('close').addEventListener('click', () => s1.call('plan.close'));
s1.on('plan.changed', render);
render(s1.call('plan.state'));
