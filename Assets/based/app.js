const $ = id => document.getElementById(id);
let current = null;
let drawn = '';

function div(className, text) {
  const node = document.createElement('div');
  node.className = className;
  if (text) node.textContent = text;
  return node;
}

function place(node, box) {
  node.style.left = box.left + 'px';
  node.style.top = box.top + 'px';
  node.style.width = box.width + 'px';
  node.style.height = box.height + 'px';
  return node;
}

function show(id, visible) {
  $(id).style.display = visible ? 'flex' : 'none';
}

function drawSteps(steps) {
  const host = $('steps');
  host.replaceChildren();
  for (const step of steps) {
    const node = div('step ' + step.state);
    node.appendChild(div('box', step.state === 'done' ? 'X' : ''));
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
  for (const fill of plan.fills) host.appendChild(place(div('fill ' + fill.kind), fill));
  for (const wall of plan.walls) host.appendChild(place(div('wall'), wall));
  for (const cut of plan.cuts) host.appendChild(place(div('cut'), cut));
  for (const label of plan.labels) {
    const node = place(div('label'), label);
    node.appendChild(div('area', label.area));
    node.appendChild(div('name', label.name));
    host.appendChild(node);
  }
}

function render(raw) {
  if (raw === null || raw === undefined) { console.error('The construction plan returned no state.'); return; }
  try {
    const data = JSON.parse(raw);
    current = data;
    $('shade').className = data.wall ? 'shade wall' : 'shade';
    $('title').textContent = data.title;
    $('status').textContent = data.status;
    $('progress').textContent = data.progress;
    $('rights-label').textContent = data.rightsLabel;
    $('rights').textContent = data.rights;
    $('construction-label').textContent = data.constructionLabel;
    $('construction').textContent = data.construction;
    $('rights-row').className = data.rightsPaid ? 'cost paid' : 'cost';
    $('construction-row').className = data.constructionPaid ? 'cost paid' : 'cost';
    $('duration').textContent = data.duration;
    $('cash').textContent = data.cash;
    $('notice').textContent = data.notice;
    $('payment').textContent = data.payment;
    $('buy').textContent = data.buy;
    $('buy').disabled = !data.canBuy;
    $('take').textContent = data.take;
    show('buy', data.canOrder);
    show('take', data.take.length > 0);
    show('report', !data.ordering);
    // Once the bills are paid, the wall page gives their room to the progress and the hold list.
    show('costs', !data.wall || data.ordering);
    show('notice', data.notice.length > 0);
    show('payment', data.payment.length > 0);
    const notes = $('notes');
    notes.replaceChildren();
    for (const line of data.notes) notes.appendChild(div('note', line));
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
$('take').addEventListener('click', () => {
  if (s1.call('plan.take') === null) console.error('Taking the project folder returned no answer.');
  render(s1.call('plan.state'));
});
$('close').addEventListener('click', () => s1.call('plan.close'));
s1.on('plan.changed', render);
render(s1.call('plan.state'));
