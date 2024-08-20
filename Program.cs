using System;
using System.IO;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLog;
using Quartz;
using Quartz.Impl;

class Program
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger(); //初始化記錄器
    #region 名詞解說
    /*
    == 
        ●同步 synchronous ： 一行程式碼執行完才會再執行下一行。但(耗時任務)時間過久使用者會以為是當機，於是有了非同步。
        ●非同步 asynchronous ：非同步在背景操作，主線程可執行其他工作(但非可執行下一行程式)，需待非同步做完後 才會再做主線程的下一行程式。
    == .Net
        ●await：為一種語法
                1.await 有在使用函式時宣告，就會先將其做完處理，才會再做後續的。(延遲大才會看的出結果)
                2.await 有在使用函式時宣告，才承認功能，不然沒宣告就算函式內有await 也不理會。
                3.遇同步一定執行完後才做下一步。
                  非同步await在背景執行時，主線程一樣可執行(其他工作)，但非他會直接執行下行程式！
                  仍需等待await做完後才會執行主線程的下行程式。

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
    #endregion

    static async Task Main(string[] args)  //async為使用非同步函式，搭配 await 語法。以Task返回結果。
    {        
        //Directory.GetCurrentDirectory()：預設根目錄(通常.sln 文件所在的目錄)。但有更改nlog.config為相對路徑，所以就依相對路徑顯示。
        Console.WriteLine("Current Directory: " + Directory.GetCurrentDirectory());

        #region 設定log檔名稱及位置
        //使用 ConfigurationBuilder：1.告知路徑 2.讀取 JSON 設定檔(檔名jsconfig1.json)。
        //不可在根目錄和相對路徑都同時存著.json。這樣刪掉相對路徑，程式會自己長出來。造成 optional 失效。
        var configuration = new ConfigurationBuilder()
            //.SetBasePath(Directory.GetCurrentDirectory()) 
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory) // 設定根目錄為基準目錄
            .AddJsonFile("jsconfig1.json", optional: false, reloadOnChange: true) //檔案配置名稱 //optional：false，如果配置文件不存在，應用程式將拋出錯誤，並停止運行。 //reloadOnChange：當配置文件內容發生變化時，是否自動重新載入配置 →仍有錯
            .Build();
        #endregion

        /*
        #region 監控配置文件的變更
        // 使用 FileSystemWatcher 監控配置文件的變更
        var fileWatcher = new FileSystemWatcher(AppDomain.CurrentDomain.BaseDirectory, "jsconfig1.json");
        fileWatcher.Changed += (sender, eventArgs) =>
        {
            Console.WriteLine("Configuration file changed. Reloading...");
            // 手動重新加載配置
            configuration.Reload();
            // 重新獲取設定值，假設需要的話
            var settings = configuration.GetSection("Settings").Get<SettingTime[]>();
            // 可以在這裡重新排程或更新其他相關的操作
        };
        fileWatcher.EnableRaisingEvents = true;
        #endregion
        */

        // IScheduler 排程任務 預設排程器(從StdSchedulerFactory取得)
        // 可在 Main 方法中進行一次，確保所有排程任務都使用同一個排程器實例 // IScheduler可以使用多個，但通常不建議。因為增加系統複雜和資源需求。(在多個不同task使用一個，也算多個)
        IScheduler scheduler = await StdSchedulerFactory.GetDefaultScheduler();
        await scheduler.Start(); //初始化及啟動排程器

        //將任務內容做執行。從配置中(jsconfig1.json)提取名為 "Settings" 的節點，並將將此區段轉換為 Setting[] 陣列
        var Settings = configuration.GetSection("Settings").Get<SettingTime[]>();
        foreach (var setting in Settings)
        {
            if (setting.Type == 1)//每隔幾秒記錄
            {
                ScheduleIntervalTask(setting.IntervalSeconds, scheduler); //改同步
            }
            else if (setting.Type == 2)//指定時間記錄
            {
                await ScheduleSpecificTimeTask(setting.ScheduledTime, scheduler);                
            }
        }

        Console.ReadLine();
    }

    /// <summary>
    /// 每隔?秒就記錄
    /// </summary>
    /// <param name="intervalSeconds"></param>
    /// <returns></returns>
    private static async Task ScheduleIntervalTask(int intervalSeconds, IScheduler scheduler)
    {   //await scheduler.Start(); //初始化及啟動排程器

        #region 工作詳細信息定義
        IJobDetail job = JobBuilder.Create<LogShow>()
            .WithIdentity("intervalJob", "group1")  //設置工作的名稱,組名 //名稱+組名為key。不同job若名稱組名一樣 後者會蓋掉前者→所以不要用相同 //但可以同組名 不同名稱 會認為不同
            .Build();
        #endregion

        #region 設定觸發器
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("intervalTrigger", "group1") //觸發器的名稱,ID ，概同job
                                                       //.StartNow() //設定觸發器從當前時間開始立即啟動。若重覆疑慮可不用執行。
            .WithSimpleSchedule(x => x
                .WithIntervalInSeconds(intervalSeconds) //設定觸發器的執行間隔，單位為秒。
                .RepeatForever()) //設定觸發器永遠重複執行，除非手動停止
            .Build(); //放最後，因為要整合參數
        #endregion

        await scheduler.ScheduleJob(job, trigger);
        //scheduler.PauseAll(); //有打開 會抓到第一次
        /* 排程器 暫停又打開 測試應該沒有關係
        //scheduler.PauseAll();
        //await Task.Delay(TimeSpan.FromSeconds(1));
        //await scheduler.ResumeAll();
        */
    }

    /// <summary>
    /// 於指定時間記錄
    /// </summary>
    /// <param name="scheduledTime"></param>
    /// <returns></returns>
    private static async Task ScheduleSpecificTimeTask(DateTime scheduledTime , IScheduler scheduler)
    {   //await scheduler.Start(); //初始化及啟動排程器

        IJobDetail job = JobBuilder.Create<LogShow>()
            .WithIdentity("specificTimeJob", "group2")
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("specificTimeTrigger", "group2")
            .StartAt(scheduledTime)
            .WithSimpleSchedule(x => x.WithMisfireHandlingInstructionIgnoreMisfires()) // 忽略已錯過的時間，不立即執行
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

public class LogShow : IJob
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static DateTime? lastPrintedTime = null; // DateTime?：通常不能為null。此處設定為可以null，然後用此判斷 是否有打印過 或是 時間過長
    private static int printCount = 0; // 計時器

    public Task Execute(IJobExecutionContext context)
    {
        var currentTime = DateTime.Now;
        if (lastPrintedTime == null || !lastPrintedTime.Value.Equals(currentTime)) //目前無效 //為了不重覆時間打印：判斷 當是null 或是 不為當下時間 才打印
        {
            printCount++;// 每次執行時增加計數器
            Logger.Info($"[第{printCount}次] Current Time : {currentTime}"); //存成log記錄內容的字樣
            Console.WriteLine($"[第{printCount}次] Current Time : {currentTime}"); // 控制台顯示的打印字樣
            lastPrintedTime = currentTime;
        }
        return Task.CompletedTask;
    }
}


