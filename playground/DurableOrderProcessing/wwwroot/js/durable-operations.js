// 耐久operations.js:从浏览器开始并观察到永久性 HTTP 操作, 即自动。
//
// 每个用户的意图,例如检查篮子,都得到一个备用钥匙,而帮助者在发送前就坚持了这个关键
// 请求的顺序,在网络故障后,双击或页面刷新时,带有相同的键,
// 服务器将进入第一个尝试启动的操作。一旦服务器确认开始,
// 帮助助手忘记了意图的密钥, 并持续运行操作 操作位置直到操作为终端, 所以
// 刷新页面继续观察它。
//
// 协助者说 Orleans 持久作业的协议,该协议与 Azure 长期作业相符:
// 开始返回 200 确定结果, 或 202 接受, 或 424 失败依赖 。
// 操作- 运行位置; 监视器报告 { id, status, 结果、 结果、 错误}; { monitor}/ 活动将状态流到
// 服务器- 发送事件; 获取 { 监测} , 使用优先: wait= N long-polls; POST { monitor} : cancel 取消 ;
// POST {监测}/ 信号/{ name} 发送信号 。

const terminalStatuses = new Set(["Succeeded", "Failed", "Canceled"]);

/** 返回状态监视器是否为终端。 */
export const isTerminal = operation => terminalStatuses.has(operation?.status);

/** 答复既未完成也未找到行动地点。 */
export class OperationError extends Error {
  constructor(response, problem) {
    super(problem?.detail ?? problem?.title ?? `${response.status} ${response.statusText}`);
    this.name = "OperationError";
    this.status = response.status;
    this.problem = problem;
  }
}

export class DurableOperations {
  /**
   * @ 参数{ 对象} [选项]
   * @ param { storage} [options.storage] 坚持者意图和运行操作; sessionStorage 默认,
   *   复习标签。
   * @ param{string} [options.prefix] 将存储键提前固定。
   * @ param {编号} [options.retryDelays] 延迟, 以毫秒计, 每次重试请求失败前的延误
   *   网络错误或 503 服务 没有 Retry-after 无法使用。
   * @param {number} [options.wait] 秒, 长时间的民意测验要求服务器等待 。
   */
  constructor({ storage = sessionStorage, prefix = "durable-operations", retryDelays = [250, 1000, 3000], wait = 30 } = {}) {
    this.storage = storage;
    this.prefix = prefix;
    this.retryDelays = retryDelays;
    this.wait = wait;
  }

  /** 返回一个意图的持续键,为新的意图创建关键字。 */
  key(intent) {
    const record = this.#read(`intent:${intent}`) ?? { key: crypto.randomUUID() };
    this.#write(`intent:${intent}`, record);
    return record.key;
  }

  /** 忘记了意图,所以它的下一个开始得到一个新的钥匙。 */
  forget(intent) {
    this.storage.removeItem(this.#name(`intent:${intent}`));
  }

  /** 返回服务器尚未确认的启动时间: [{意向、密钥、请求}]。 */
  pending() {
    return this.#entries("intent:")
      .filter(([, record]) => record.request)
      .map(([intent, record]) => ({ intent, ...record }));
  }

  /** 返回帮助者所观察到的运行中的操作 [{位置、意图、数据}]。 */
  watching() {
    return Object.entries(this.#read("watching") ?? {}).map(([location, entry]) => ({ location, ...entry }));
  }

  /**
   * 开始实施意图,或达到先前意图尝试启动的操作。
   *
   * @ param{string} 意向书 命名用户意图, 如 `checkout:alice`。
   * @ palam {( 关键: 字符串) {{ URL: 字符串, 方法? : 字符串, body? : 未知, 数据? : 未知 } 构建请求
   *   帮助者坚持了请求,所以重新开始就发出同样的请求。
   * @ 参数{ 对象} [选项]
   * @ palam {( 尝试: 编号) } { 记录<string, string>} [options.headers加上标头来图谋
   * @ param {number} [options.wait] 开始要求服务器等待结果的秒数 。
   * @ returns {promise}} 密钥: string, 位置?: string, 操作???: 对象, 结果?: 未知, 数据?: 未知
   *   运行期间或一旦失败,或成功后的结果,操作状态监视器。
   */
  async start(intent, build, { headers, wait } = {}) {
    const name = `intent:${intent}`;
    const record = this.#read(name) ?? { key: crypto.randomUUID() };
    record.request ??= build(record.key);
    this.#write(name, record);
    return this.#send(intent, record, { headers, wait });
  }

  /**
   * 发送服务器没有确认的启动程序, 并用他们的密钥发送启动程序, 例如在 A
   * 开始。返回启动结果,按未完成()顺序排列。
   */
  async resume() {
    return Promise.all(this.pending().map(({ intent, ...record }) => this.#send(intent, record, {})));
  }

  /**
   * 观察一项运行,直至其终端为止,并有服务器已到的事件或长期民意调查,但情况却如此
   * 无法使用, 一旦它成为终端或服务器不再持有, 就会忘记它 。
   *
   * 行动状态监视器
   * @ 参数{ 对象} [选项]
   * @ palam {( 操作: 对象) { 无效} [options.onStatus接收每个状态。
   * @param {AbortSignal}options.signal] 停止观察。
   * @ returns {promise<object>} 运行终端状态监视器 。
   * @ 发射 {OperationError以404状态 当操作不存在或保留结束时
   */
  async observe(location, { onStatus, signal } = {}) {
    let operation = typeof EventSource === "undefined" ? undefined : await this.#observeEvents(location, onStatus, signal);
    try {
      while (!isTerminal(operation)) {
        signal?.throwIfAborted();
        operation = await this.get(location, { wait: this.wait, signal });
        onStatus?.(operation);
      }
    } catch (error) {
      if (error instanceof OperationError && error.status === 404) this.#unwatch(location);
      throw error;
    }

    this.#unwatch(location);
    return operation;
  }

  /** 读取操作状态监视器,在运行时用长的邮箱等待秒。 */
  async get(location, { wait = 0, signal } = {}) {
    const response = await this.#fetch(location, { headers: wait > 0 ? { Prefer: `wait=${wait}` } : {}, signal });
    return this.#readOperation(response);
  }

  /** 要求取消一项行动,并附有理由,并返回其地位监测器。 */
  async cancel(location, reason) {
    const response = await this.#fetch(`${location}:cancel`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ reason }),
    });
    return this.#readOperation(response);
  }

  /**
   * 向操作发送一个 JSON 值的信号。一个具有等值的重复信号成功,因此重试
   * 安全; 从密钥( 意图) 中获取意向值, 所以恢复的意向会发送相同的值 。
   */
  async signal(location, name, value) {
    const response = await this.#fetch(`${location}/signals/${encodeURIComponent(name)}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(value),
    });
    if (!response.ok) throw new OperationError(response, await readProblem(response));
  }

  async #send(intent, record, { headers, wait }) {
    const { url, method = "POST", body, data } = record.request;
    const response = await this.#fetch(url, {
      method,
      headers: attempt => ({
        "Idempotency-Key": record.key,
        ...(body === undefined ? {} : { "Content-Type": "application/json" }),
        ...(wait > 0 ? { Prefer: `wait=${wait}` } : {}),
        ...headers?.(attempt),
      }),
      body: body === undefined ? undefined : JSON.stringify(body),
    });

    // 服务器回答,所以它能控制启动结果 目标的钥匙已经达到了目的
    const location = response.headers.get("Operation-Location") ?? undefined;
    if (response.status === 202 || response.status === 424) {
      const operation = await response.json();
      this.forget(intent);
      if (!isTerminal(operation)) this.#watch(location, { intent, data });
      return { key: record.key, location, operation, data };
    }

    if (response.ok) {
      this.forget(intent);
      const result = response.status === 204 ? undefined : await response.json();
      return { key: record.key, location, result, data };
    }

    // 服务器错误使启动无法识别, 因此重试发送相同的密钥; 客户端错误为最终错误 。
    if (response.status < 500) this.forget(intent);
    throw new OperationError(response, await readProblem(response));
  }

  // 观察操作事件直到它为终端。当串流关闭时,返回最后状态或未定义状态
  // 终端状态之前, 例如服务器不为事件服务, 所以呼叫者长号 。
  #observeEvents(location, onStatus, signal) {
    return new Promise(resolve => {
      const events = new EventSource(`${location}/events`);
      let last;
      const finish = () => {
        events.close();
        signal?.removeEventListener("abort", finish);
        resolve(last);
      };
      signal?.addEventListener("abort", finish, { once: true });
      events.addEventListener("status", event => {
        last = JSON.parse(event.data);
        onStatus?.(last);
        if (isTerminal(last)) finish();
      });

      // 浏览器将中断的流与 Last- Event-ID 重新连接; 它关闭的流结束事件 。
      events.onerror = () => {
        if (events.readyState === EventSource.CLOSED) finish();
      };
    });
  }

  // 发送请求、重试网络故障和503服务无可用,使操作保持不变
  // 设置的延迟,或回复的 Retry-after。页眉可能会因尝试而变化。
  async #fetch(url, { method = "GET", headers = {}, body, signal } = {}) {
    for (let attempt = 0; ; attempt++) {
      let delay;
      try {
        const response = await fetch(url, { method, headers: typeof headers === "function" ? headers(attempt) : headers, body, signal });
        if (response.status !== 503 || attempt >= this.retryDelays.length) return response;
        const retryAfter = Number(response.headers.get("Retry-After"));
        delay = Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter * 1000 : this.retryDelays[attempt];
      } catch (error) {
        if (signal?.aborted || attempt >= this.retryDelays.length) throw error;
        delay = this.retryDelays[attempt];
      }

      await new Promise(resolve => setTimeout(resolve, delay));
    }
  }

  async #readOperation(response) {
    if (!response.ok) throw new OperationError(response, await readProblem(response));
    return response.json();
  }

  #watch(location, entry) {
    if (!location) return;
    const watching = this.#read("watching") ?? {};
    watching[location] = entry;
    this.#write("watching", watching);
  }

  #unwatch(location) {
    const watching = this.#read("watching") ?? {};
    delete watching[location];
    this.#write("watching", watching);
  }

  #entries(kind) {
    const prefix = this.#name(kind);
    const entries = [];
    for (let i = 0; i < this.storage.length; i++) {
      const name = this.storage.key(i);
      if (name?.startsWith(prefix)) entries.push([name.slice(prefix.length), JSON.parse(this.storage.getItem(name))]);
    }

    return entries;
  }

  #name(name) {
    return `${this.prefix}:${name}`;
  }

  #read(name) {
    const value = this.storage.getItem(this.#name(name));
    return value === null ? undefined : JSON.parse(value);
  }

  #write(name, value) {
    this.storage.setItem(this.#name(name), JSON.stringify(value));
  }
}

async function readProblem(response) {
  try {
    return response.headers.get("content-type")?.includes("json") ? await response.json() : { detail: (await response.text()) || undefined };
  } catch {
    return undefined;
  }
}
