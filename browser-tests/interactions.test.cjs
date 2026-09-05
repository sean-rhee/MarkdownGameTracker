const { test, before, after, beforeEach, afterEach } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const { spawn } = require('node:child_process');
const { chromium } = require('playwright');

let vault, server, browser, context, page, baseURL;
let pageErrors = [];
const repo = path.resolve(__dirname, '..');
const notePath = id => path.join(vault, 'Games', `${id}.md`);
const readGame = async id => {
  const response = await context.request.get(`/api/games/${encodeURIComponent(id)}`);
  assert.equal(response.status(), 200);
  return response.json();
};
const seed = (id, status = 'active', markdown = 'Keep these notes.') => fs.writeFile(
  notePath(id), `---\ntype: game\nstatus: ${status}\nplatform: PC\ncustom: preserved\n---\n${markdown}`);

before(async () => {
  vault = await fs.mkdtemp(path.join(os.tmpdir(), 'MarkdownGameTracker-browser-'));
  await fs.mkdir(path.join(vault, 'Games'));
  // Every run owns its server and vault; never attach to a user's running tracker.
  server = spawn('dotnet', ['run', '--no-build', '--no-launch-profile', '--project',
    path.join(repo, 'MarkdownGameTracker', 'MarkdownGameTracker.csproj'), '--urls', 'http://127.0.0.1:0'], {
    cwd: repo, windowsHide: true,
    env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', DOTNET_ENVIRONMENT: 'Development',
      Vault__Path: vault, Vault__GamesDirectory: 'Games', IGDB__ClientId: '', IGDB__ClientSecret: '' }
  });
  baseURL = await new Promise((resolve, reject) => {
    let output = '';
    const timer = setTimeout(() => reject(new Error(`Server startup timed out:\n${output}`)), 30000);
    const collect = chunk => {
      output += chunk.toString();
      const match = output.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/);
      if (match) { clearTimeout(timer); resolve(match[1]); }
    };
    server.stdout.on('data', collect);
    server.stderr.on('data', collect);
    server.once('error', error => { clearTimeout(timer); reject(error); });
    server.once('exit', code => { clearTimeout(timer); reject(new Error(`Server exited (${code}):\n${output}`)); });
  });
  browser = await chromium.launch({ headless: true, ...(process.env.BROWSER_CHANNEL ? { channel: process.env.BROWSER_CHANNEL } : {}) });
});

beforeEach(async () => {
  // Fixtures from one scenario must not affect another scenario's search results.
  for (const name of await fs.readdir(path.join(vault, 'Games'))) {
    await fs.unlink(path.join(vault, 'Games', name));
  }
  context = await browser.newContext({ baseURL, viewport: { width: 1440, height: 1000 } });
  page = await context.newPage();
  page.setDefaultTimeout(10000);
  pageErrors = [];
  page.on('pageerror', error => pageErrors.push(error.message));
});

afterEach(async () => {
  await context?.close();
  assert.deepEqual(pageErrors, [], 'No uncaught browser JavaScript errors');
});

after(async () => {
  await browser?.close();
  if (server && server.exitCode === null) {
    const exited = new Promise(resolve => server.once('exit', resolve));
    if (process.platform === 'win32') {
      await new Promise(resolve => {
        const killer = spawn('taskkill', ['/pid', String(server.pid), '/t', '/f'], { windowsHide: true });
        killer.once('exit', resolve);
        killer.once('error', resolve);
      });
    } else server.kill('SIGTERM');
    await exited;
  }
  // Delete only the temporary directory created by this run.
  if (vault && path.dirname(vault) === path.resolve(os.tmpdir()) && path.basename(vault).startsWith('MarkdownGameTracker-browser-')) {
    await fs.rm(vault, { recursive: true, force: true });
  }
});

test('library sorting persists through search, tabs, reload, and returning home', async () => {
  await seed('Alpha', 'active', 'Shared notes');
  await seed('Zulu', 'active', 'Shared notes');
  await seed('Finished', 'completed', 'Shared notes');
  await page.goto('/');
  await page.getByLabel('Sort by').selectOption('title-desc');
  await page.waitForURL(url => url.searchParams.get('Sort') === 'title-desc');
  assert.deepEqual(await page.locator('[data-game-card] h3').allTextContents(), ['Zulu', 'Alpha']);
  await page.getByRole('combobox', { name: 'Search', exact: true }).fill('Shared');
  await page.reload();
  assert.equal(await page.getByLabel('Sort by').inputValue(), 'title-desc');
  await page.getByRole('navigation', { name: 'Game status' }).getByRole('link', { name: 'Completed' }).click();
  assert.equal(await page.getByLabel('Sort by').inputValue(), 'title-desc');
  assert.equal(await page.getByRole('combobox', { name: 'Search', exact: true }).inputValue(), 'Shared');
  await page.goto('/');
  assert.equal(await page.getByLabel('Sort by').inputValue(), 'title-desc');
  assert.deepEqual(await page.locator('[data-game-card] h3').allTextContents(), ['Zulu', 'Alpha']);
  await page.setViewportSize({width: 390, height: 844});
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
});

test('editor preview, save, clear, and delete confirmation persist the intended content', async () => {
  await page.goto('/Games/Create');
  await page.getByLabel('Game title', { exact: true }).fill('Browser Journal');
  const editor = page.locator('textarea');
  await editor.fill('Journal');
  await editor.press('ControlOrMeta+a');
  await page.getByRole('button', { name: 'Bold', exact: true }).click();
  assert.equal(await editor.inputValue(), '**Journal**');
  await page.locator('[data-markdown-pane="preview"] strong').waitFor();
  assert.equal(await page.locator('[data-markdown-pane="preview"] strong').innerText(), 'Journal');
  await page.getByRole('button', { name: 'Create game note' }).click();
  await page.waitForURL('**/Games/Details/**');
  assert.equal((await readGame('Browser Journal')).markdown, '**Journal**');

  await page.goto('/Games/Edit/Browser%20Journal');
  await page.locator('textarea').fill('');
  await page.getByRole('button', { name: 'Save changes' }).click();
  await page.waitForURL('**/Games/Details/**');
  assert.equal((await readGame('Browser Journal')).markdown, '');

  await page.locator('[data-bs-target="#deleteModal"]').click();
  await page.getByRole('button', { name: 'Keep it' }).click();
  await page.locator('#deleteModal').waitFor({ state: 'hidden' });
  assert.equal((await readGame('Browser Journal')).title, 'Browser Journal');
  await page.locator('[data-bs-target="#deleteModal"]').click();
  await page.getByRole('button', { name: 'Delete note', exact: true }).click();
  await page.waitForURL(url => url.pathname === '/');
  await assert.rejects(fs.access(notePath('Browser Journal')), { code: 'ENOENT' });
});

test('inline rating and status menus save without altering notes or custom metadata', async () => {
  await seed('Browser Inline');
  await page.goto('/Games/Details/Browser%20Inline');
  const rating = page.getByRole('button', { name: 'Change rating for Browser Inline' });
  await rating.click();
  await page.getByLabel('Rating out of 10').fill('8.5');
  await Promise.all([page.waitForNavigation(), page.getByRole('button', { name: 'Save', exact: true }).click()]);
  assert.equal((await readGame('Browser Inline')).rating, 8.5);
  await rating.click();
  await Promise.all([page.waitForNavigation(), page.getByRole('button', { name: 'Clear', exact: true }).click()]);
  assert.equal((await readGame('Browser Inline')).rating, null);
  await page.getByRole('button', { name: 'Change status for Browser Inline' }).click();
  await Promise.all([page.waitForNavigation(), page.getByRole('button', { name: 'Completed', exact: true }).click()]);
  const game = await readGame('Browser Inline');
  assert.equal(game.status, 'completed');
  assert.equal(game.markdown, 'Keep these notes.');
  assert.equal(game.frontmatter.custom, 'preserved');
});

test('preview failures retain the draft and recover on the next edit', async () => {
  await page.route('**/api/markdown/preview', route => route.fulfill({ status: 503, body: 'Unavailable' }));
  await page.goto('/Games/Create');
  const editor = page.locator('textarea');
  await editor.fill('Draft survives');
  await page.getByText('Preview unavailable', { exact: true }).waitFor();
  assert.equal(await editor.inputValue(), 'Draft survives');
  await page.unroute('**/api/markdown/preview');
  await editor.fill('**Recovered draft**');
  const preview = page.locator('[data-markdown-pane="preview"] strong');
  await preview.waitFor();
  assert.equal(await preview.innerText(), 'Recovered draft');
  assert.equal(await editor.inputValue(), '**Recovered draft**');
  assert.deepEqual(await fs.readdir(path.join(vault, 'Games')), []);
});

test('search filters notes immediately, supports keyboard selection, and survives reload', async () => {
  await seed('Browser Search Alpha', 'active', 'A moon journal');
  await seed('Browser Search Beta', 'active', 'A sun journal');
  await seed('Browser Search Completed', 'completed', 'A moon journal');
  await page.goto('/?status=active');
  const search = page.locator('[data-search-suggestions] input');
  await search.fill('moon');
  await page.locator('[data-game-id="Browser Search Beta"]').waitFor({ state: 'hidden' });
  assert.equal(await page.locator('[data-game-card]:visible').count(), 1);
  assert.match(await page.locator('[data-game-card]:visible').innerText(), /Browser Search Alpha/);
  assert.equal(new URL(page.url()).searchParams.get('search'), 'moon');
  await page.reload();
  assert.equal(await page.locator('[data-game-card]:visible').count(), 1);
  await search.fill('Browser Search A');
  await search.press('ArrowDown');
  await search.press('Enter');
  assert.equal(await search.inputValue(), 'Browser Search Alpha');
  assert.equal(await search.getAttribute('aria-expanded'), 'false');
  await search.fill('');
  assert.equal(await page.locator('[data-game-card]:visible').count(), 2);
});

test('rename conflict keeps the edit form and preserves both files', async () => {
  await fs.writeFile(notePath('Browser Old (PC)'), '---\ntype: game\ntitle: Browser Old\nstatus: active\n---\nOriginal');
  await seed('Browser New (PC)');
  const before = await Promise.all(['Browser Old (PC)', 'Browser New (PC)'].map(id => fs.readFile(notePath(id), 'utf8')));
  await page.goto('/Games/Edit/Browser%20Old%20%28PC%29');
  await page.getByLabel('Game title', { exact: true }).fill('Browser New');
  await page.locator('textarea').fill('Unsaved edits');
  await Promise.all([page.waitForNavigation(), page.getByRole('button', { name: 'Save changes' }).click()]);
  await page.getByText(/A game note named .* already exists/).waitFor();
  assert.equal(await page.locator('textarea').inputValue(), 'Unsaved edits');
  assert.deepEqual(await Promise.all(['Browser Old (PC)', 'Browser New (PC)'].map(id => fs.readFile(notePath(id), 'utf8'))), before);
});
