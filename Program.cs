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

    static async Task Main(string[] args) //async非同步，允許使用 await 關鍵字。task返回一個任數。
    {
        // 打印當前工作目錄，確認基目錄位置
        Console.WriteLine("Current Directory: " + Directory.GetCurrentDirectory());

        //使用 ConfigurationBuilder 讀取 JSON 設定檔並解析成 Setting 類別的數組
        ////未指定要執行的 type，則程式將執行所有配置中指定的任務
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory()) //設置配置文件的基路徑為當前目錄
            .AddJsonFile("jsconfig1.json",optional:false, reloadOnChange:true) //檔案配置名稱
            .Build();

        var settings = configuration.GetSection("Settings").Get<Setting[]>();

        foreach (var setting in settings)
        {
            if (setting.Type == 1)
            {
                await ScheduleIntervalTask(setting.IntervalSeconds); //每隔幾秒
            }
            else if (setting.Type == 2)
            {
                await ScheduleSpecificTimeTask(setting.ScheduledTime); //指定時間
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
        // 實現每間隔指定時間打印當下時間並記錄至Log
        // Quartz scheduler
        IScheduler scheduler = await StdSchedulerFactory.GetDefaultScheduler();
        await scheduler.Start();

        //工作詳細信息定義
        //WithIdentity 方法用來設置這個工作的唯一標識符（ID）和工作組
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

        IJobDetail job = JobBuilder.Create<SpecificTimeJob>() //為指定時間
            .WithIdentity("specificTimeJob", "group2")
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("specificTimeTrigger", "group2")
            .StartAt(scheduledTime)
            .Build();

        await scheduler.ScheduleJob(job, trigger);
    }
}

public class Setting
{
    /// <summary>
    /// Type1每秒、Type2指定時間。沒指定type，都執行。
    /// </summary>
    public int Type { get; set; }
    /// <summary>
    /// 每隔?秒
    /// </summary>
    public int IntervalSeconds { get; set; }
    public DateTime ScheduledTime { get; set; }
}

public class IntervalJob : IJob
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public Task Execute(IJobExecutionContext context)
    {
        var currentTime = DateTime.Now;
        Logger.Info($"Current Time: {currentTime}");
        Console.WriteLine($"Current Time: {currentTime}");
        return Task.CompletedTask;
    }
}

public class SpecificTimeJob : IJob
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public Task Execute(IJobExecutionContext context)
    {
        var currentTime = DateTime.Now;
        Logger.Info($"Current Time: {currentTime}");
        Console.WriteLine($"Current Time: {currentTime}");
        return Task.CompletedTask;
    }
}
