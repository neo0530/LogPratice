using System;
using System.IO;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using NLog;
using Quartz;
using Quartz.Impl;

class Program
{
    private static DateTime? lastReloadTime = null; // DateTime?：通常不能為null。此處設定為可以null，然後用此判斷 是否有打印過 或是 時間過長  

    static async Task Main(string[] args) //靜態(沒寫就是動態) 非同步 任務 //string[] args 為main 方法，接輸入參數 會以string陣列存
    {
        Console.WriteLine("Current Directory: " + Directory.GetCurrentDirectory());

        #region 加載配置（AddJsonFile）-設定json檔名稱及位置
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("jsconfig1.json", optional: false, reloadOnChange: true) 
            .Build();
        //將任務內容(jsconfig1.json)抓 "Settings"節點，並轉換為 Setting[] 陣列
        var Settings = configuration.GetSection("Settings").Get<SettingTime[]>();
        #endregion

        #region ★配置變更監控與重新排程
        ChangeToken.OnChange(() => configuration.GetReloadToken(), async () =>
        {
            var currentReloadTime = DateTime.Now;
            if (lastReloadTime == null || (currentReloadTime - lastReloadTime) > TimeSpan.FromSeconds(1)) // ★會因為背後運作作業，造成重送。所以做1秒後才再重排 
            {
                lastReloadTime = currentReloadTime;
                Console.WriteLine("Configuration 更新中, 請稍等...");

                // 重新加載配置
                var configuratio_1 = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("jsconfig1.json", optional: false, reloadOnChange: true) 
                    .Build();
                var newSettings = configuratio_1.GetSection("Settings").Get <SettingTime[]>();

                #region 初始化排程器（IScheduler）        
                IScheduler scheduler1 = await StdSchedulerFactory.GetDefaultScheduler();
                IScheduler scheduler2 = await StdSchedulerFactory.GetDefaultScheduler();
                // ★清除舊的作業和觸發器
                await scheduler1.Clear();
                await scheduler2.Clear();
                //初始化及啟動排程器
                await scheduler1.Start(); 
                await scheduler2.Start();                 
                #endregion

                // 重新排程任務
                #region 任務類型
                foreach (var setting in newSettings)
                {
                    if (setting.Type == 1)//每隔幾秒記錄
                    {
                        await ScheduleIntervalTask(setting.IntervalSeconds, scheduler1);
                    }
                    else if (setting.Type == 2)//指定時間記錄
                    {
                        if (setting.ScheduledTime > lastReloadTime) //★判斷指定時間有大於現在時間才執行
                            await ScheduleSpecificTimeTask(setting.ScheduledTime, scheduler2);
                    }
                }
                #endregion
            }
        });
        #endregion

        #region 初始化排程器（IScheduler）        
        IScheduler scheduler1 = await StdSchedulerFactory.GetDefaultScheduler();
        IScheduler scheduler2 = await StdSchedulerFactory.GetDefaultScheduler();
        await scheduler1.Start(); //初始化及啟動排程器        
        await scheduler2.Start(); //初始化及啟動排程器
        #endregion

        #region 任務類型
        foreach (var setting in Settings)
        {
            if (setting.Type == 1)//每隔幾秒記錄
            {
                await ScheduleIntervalTask(setting.IntervalSeconds, scheduler1);
            }
            else if (setting.Type == 2)//指定時間記錄
            {
                if (setting.ScheduledTime > lastReloadTime) //★判斷指定時間有大於現在時間才執行
                    await ScheduleSpecificTimeTask(setting.ScheduledTime, scheduler2);
            }
        }
        #endregion

        Console.ReadLine();
    }

    /// <summary>
    /// 每隔?秒就記錄
    /// </summary>
    /// <param name="intervalSeconds"></param>
    /// <returns></returns>
    private static async Task ScheduleIntervalTask(int intervalSeconds, IScheduler scheduler)
    {   
        #region 創建任務-工作詳細信息定義
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

        Console.WriteLine($"排程 Type 1 任務：每 {intervalSeconds} 秒執行一次");
        //調度任務
        await scheduler.ScheduleJob(job, trigger);
    }

    /// <summary>
    /// 於指定時間記錄
    /// </summary>
    /// <param name="scheduledTime"></param>
    /// <returns></returns>
    private static async Task ScheduleSpecificTimeTask(DateTime scheduledTime, IScheduler scheduler)
    {
        IJobDetail job = JobBuilder.Create<LogShow>()
            .WithIdentity("specificTimeJob", "group2")
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("specificTimeTrigger", "group2")
            .StartAt(scheduledTime)
            //.WithSimpleSchedule(x => x.WithMisfireHandlingInstructionIgnoreMisfires()) // 忽略已錯過的時間，不立即執行
            .Build();

        Console.WriteLine($"排程 Type 2 任務：預定於 {scheduledTime} 執行");
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
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger(); //初始化記錄器
    private static DateTime? lastPrintedTime = null;   
    private static int printCount = 0; // 計時器

    public Task Execute(IJobExecutionContext context) //因為Quartz.NET套件的關係，排程器觸發任務時，會自動調用Execute，不需呼叫
    {
        var currentTime = DateTime.Now;
        if (lastPrintedTime == null || !lastPrintedTime.Value.Equals(currentTime)) //為了不重覆時間打印：判斷 當是null 或是 不為當下時間 才打印
        {
            printCount++;// 每次執行時增加計數器
            Logger.Info($"[第{printCount}次] Current Time : {currentTime}"); //存成log記錄內容的字樣
            Console.WriteLine($"[第{printCount}次] Current Time : {currentTime}"); // 控制台顯示的打印字樣
            lastPrintedTime = currentTime;
        }
        return Task.CompletedTask;
    }
}


