const $ = id => document.getElementById(id);
let current = null;
function render(raw) {
  if (!raw) { console.error('The construction plan returned no state.'); return; }
  try {
    const data = JSON.parse(raw);
    current = data;
    const labels = data.labels;
    $('title').textContent = data.title;
    $('close').textContent = labels.close;
    $('existing').textContent = labels.existing;
    $('expansion').textContent = labels.expansion;
    $('details').textContent = labels.details;
    $('status').textContent = data.status;
    $('rights-label').textContent = labels.rights;
    $('construction-label').textContent = labels.construction;
    $('rights').textContent = '$' + data.rights;
    $('construction').textContent = '$' + data.construction;
    $('description').textContent = data.stage === 'RightsMissing' ? labels.rightsDetail : labels.buildDetail;
    $('duration').textContent = data.stage === 'Constructing' ? data.nights + ' / 4' : labels.nights;
    $('delivery').textContent = labels.delivery;
    $('notice').textContent = data.notice;
    $('payment').textContent = data.payment;
    $('buy').disabled = !data.canBuy;
    $('buy').textContent = data.stage === 'RightsMissing' ? labels.buyRights
      : data.stage === 'RightsOwned' ? labels.buyConstruction
      : data.stage === 'Complete' ? labels.complete : labels.working;
  } catch (error) { console.error('Construction plan render failed: ' + error.message); }
}
$('buy').addEventListener('click', () => {
  if (!current || !current.canBuy) return;
  $('buy').disabled = true;
  const answer = s1.call('plan.buy', current.stage === 'RightsMissing' ? 'rights' : 'construction');
  if (!answer) console.error('The purchase request returned no acknowledgement.');
});
$('close').addEventListener('click', () => s1.call('plan.close'));
s1.on('plan.changed', render);
render(s1.call('plan.state'));
