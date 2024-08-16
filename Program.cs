using System;
using System.IO;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLog;
using Quartz;
using Quartz.Impl;

class Program
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger(); //初始化記錄器

    /*
    == JavaScript
        ●同步 synchronous ：(JavaScript 是單執行緒的程式語言) 一行程式碼執行完才會再執行下一行。但(耗時任務)時間過久使用者會以為是當機，於是有了非同步。
        ●非同步 asynchronous ：
            (1)執行時不必待程式完成，而是繼續執行下面代碼，直到任務完成再通知。(如 文件操作、數據庫操作、AJAX 以及定時器)
                非同步的程式碼或事件，並不會阻礙主線程執行其他程式碼。
            (2)JavaScript 有兩種實現非同步的方式：
                1.回調函式 callback function(缺點：callback hell)  2.promise (演變優化第一種產生的)
        ●promise：(可看作是一種處理非同步操作結果的方式)。這個約定請求會在未來每個時刻返回數據給調用者。Promise 是用來表示一個非同步操作的最终完成（或失敗）及其结果值。
    == .Net
        ●await：為一種語法
                →與async 搭配透過await 知道斷點在哪，於此要呈現結果後，才再繼續執行主線程！
                在非同步函式中我們可以調用其他的非同步函式，使用 await 語法會等待 Task 完成之後直接返回最終的結果。

        ●Task ：(為非同步不論有無使用 async).NET版的promise。
                可以返回一個結果 (Task<T>)，或者表示一個完成的操作 (Task)。如果需要返回值，比如一個整數，會使用 Task<int>；如果沒有返回值，就用 Task。
                → 等同「預約」了一個未來某時刻會完成的操作。這個預約的操作會立即開始執行，但它的結果（或影響）不會立即反映在程序中。
                → 結果是當有遇到awit的時候才會呈現。
    ==
        ●IScheduler ：IScheduler可以使用多個，但通常不建議。因為增加系統複雜和「資源需求」。(在多個不同task使用一個，也算多個)
        ●IJobDetail、ITrigger：不建議使用多個於同一個task，主要是管理複雜度以及千擾性的問題。
     */
    /*
        實務：(1)通常不多個非同步 (2)非同步通常耗時，所以會放在同步之前
        同步一定做完，才會做之後的事。
        整個線程執行的順序是依序沒錯，但因為遇到非同步所以回來的結果順序就不一定依序。        
     */

    static async Task Main(string[] args)  //async為可使用異位函式，搭配 await 語法。以Task返回結果。
    {
        // 打印(由Directory.GetCurrentDirectory取得)當前工作目錄路徑，確認基目錄位置 (根目錄通常 .sln 文件所在的目錄就是專案的根目錄)
        Console.WriteLine("Current Directory: " + Directory.GetCurrentDirectory());

        //使用 ConfigurationBuilder：1.路徑為基目錄 2.讀取 JSON 設定檔(檔名jsconfig1.json)。
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory()) //設置配置文件的基路徑為當前目錄
            .AddJsonFile("jsconfig1.json",optional:false, reloadOnChange:true) //檔案配置名稱 //optional：false，如果配置文件不存在，應用程式將拋出錯誤，並停止運行。 //reloadOnChange：當配置文件內容發生變化時，是否自動重新載入配置
            .Build();

        //從配置中(jsconfig1.json)提取名為 "Settings" 的節點，並將將此區段轉換為 Setting[] 陣列
        var Settings = configuration.GetSection("Settings").Get<SettingTime[]>();

        foreach (var setting in Settings)
        {
            if (setting.Type == 1)//每隔幾秒記錄
            {
                await ScheduleIntervalTask(setting.IntervalSeconds); 
            }
            else if (setting.Type == 2)//指定時間記錄
            {
                await ScheduleSpecificTimeTask(setting.ScheduledTime); 
            }
        }

        Console.ReadLine();
    }

    /// <summary>
    /// 每隔?秒就記錄
    /// </summary>
    /// <param name="intervalSeconds"></param>
    /// <returns></returns>
    private static async Task ScheduleIntervalTask(int intervalSeconds)
    {
        // 每間隔指定時間打印當下時間並記錄至Log // Quartz scheduler
        // 從StdSchedulerFactory取得一個預設排程器，設定為非同步方式。 // 代表會回一個Task<IScheduler>，而非直接返回IScheduler。這樣可以避免程式阻塞，並允許在等待排程器創建的同時執行其他操作。
        // IScheduler可以使用多個，但通常不建議。因為增加系統複雜和資源需求。(在多個不同task使用一個，也算多個)
        IScheduler scheduler = await StdSchedulerFactory.GetDefaultScheduler(); 
        await scheduler.Start();

        //工作詳細信息定義
        //WithIdentity 方法用來設置 工作的唯一標識符（ID）和工作組
        IJobDetail job = JobBuilder.Create<IntervalJob>()
            .WithIdentity("intervalJob", "group1")  //工作的名稱,組名
            .Build();

        //設定觸發器
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("intervalTrigger", "group1") //觸發器的名稱,組名
            .StartNow() //設定觸發器從當前時間開始立即啟動
            .WithSimpleSchedule(x => x
                .WithIntervalInSeconds(intervalSeconds) //設定觸發器的執行間隔，單位為秒。intervalSeconds 是從設定檔或程式碼中取得的間隔時間
                .RepeatForever()) //設定觸發器永遠重複執行，除非手動停止
            .Build();

        await scheduler.ScheduleJob(job, trigger);
    }

    /// <summary>
    /// 於指定時間記錄
    /// </summary>
    /// <param name="scheduledTime"></param>
    /// <returns></returns>
    private static async Task ScheduleSpecificTimeTask(DateTime scheduledTime)
    {
        // 實現特定時間打印當下時間並記錄至Log
        // Quartz scheduler
        IScheduler scheduler = await StdSchedulerFactory.GetDefaultScheduler();
        await scheduler.Start();

        IJobDetail job = JobBuilder.Create<IntervalJob>() //為指定時間
            .WithIdentity("specificTimeJob", "group2")
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("specificTimeTrigger", "group2")
            .StartAt(scheduledTime)
            .Build();

        await scheduler.ScheduleJob(job, trigger);
    }
}

public class SettingTime
{
    /// <summary>
    /// Type1每秒、Type2指定時間。沒指定type，都執行。
    /// </summary>
    public int Type { get; set; }
    /// <summary>
    /// 每隔?秒
    /// </summary>
    public int IntervalSeconds { get; set; }
    /// <summary>
    /// 指定時間
    /// </summary>
    public DateTime ScheduledTime { get; set; }
}

public class IntervalJob : IJob
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public Task Execute(IJobExecutionContext context)
    {
        var currentTime = DateTime.Now;
        Logger.Info($"Current Time : {currentTime}"); //存成log的字樣
        Console.WriteLine($"Current Time : {currentTime}"); // 控制台顯示的打印字樣
        return Task.CompletedTask;
    }
}


